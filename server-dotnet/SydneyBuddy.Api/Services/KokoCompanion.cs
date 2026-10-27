using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SydneyBuddy.Api;

// Opt-in iOS companion experience. Feelings stay in trip fields; this never writes memory.
public static class KokoCompanion
{
    public const string Prompt="""
先接住用户明确表达的心情，再协助旅行。疲惫时可以提议休息和留白，不推断健康诊断或长期体力；想吃点好的不等于提高预算。拒绝、取消或改主意不需要理由，不失望、不说服、不用内疚或断签施压。
只引用当前有效的已确认记忆，准确保留条件，允许“这次不一样”。用户本次心情与临时变化只能写本次 fields，不能保存成长期画像；长期改变仍必须走可选确认卡。
温柔可以具体而简短，偶尔有一点小馋或小幽默，每轮不重复口头禅。不声称已修改正式行程、保存记忆或核实地点，除非对应操作真正成功。信息够了直接生成，不额外追问心情或记忆。
""";
    public static bool Enabled(PetSession s)=>s.Dialogue?.CompanionVersion==1;
    public static PetQuickReply[] MoodReplies()=>[Q("想歇一歇","mood_rest"),Q("想吃点好的","mood_food"),Q("想找点新鲜感","mood_explore"),Q("先聊旅行","mood_skip")];
    public const string Greeting="小背包带好啦。你这次想歇一歇、吃点好的，还是找点新鲜感？也可以直接告诉我想去哪儿。";
    private static PetQuickReply Q(string text,string intent)=>new(Guid.NewGuid().ToString("N"),text,intent);
    private static JsonObject Turn(string reply,params PetQuickReply[] options)=>new(){["reply"]=reply,["companionTurn"]=true,["companionReplies"]=System.Text.Json.JsonSerializer.SerializeToNode(options,JsonExtensions.Options)};
    public static PetQuickReply[] Options(JsonObject result)=>result["companionReplies"]!.Deserialize<PetQuickReply[]>(JsonExtensions.Options)!;
    private static JsonObject Next(string text)=>new(){["reply"]=text,["readyForDraft"]=true,["followTripStep"]=true};
    private sealed record Recall(string Key,string Value,string SourceId);
    private static Recall? CurrentRecall(PetSession s)
    {
        foreach(var key in new[]{"rhythm","daily_steps"}) {
            var facts=s.ConfirmedDna.Where(x=>x.Key==key && x.Status=="confirmed" && x.Source!="ai_inference" && x.Condition.Length==0 && x.MemoryId.Length>0 && Valid(key,x.Value)).ToArray();
            if(facts.Select(x=>x.Value).Distinct().Count()==1)return new(key,facts[0].Value,facts[0].MemoryId);
        }
        // Read the current saved records, never a cached or client-supplied profile or AI guess.
        foreach(var (key,property) in new[]{("rhythm","rhythm"),("daily_steps","dailySteps")}) {
            if(s.ConfirmedDna.Any(x=>x.Key==key))continue;
            var values=s.ActiveMemories.OfType<JsonObject>().Select(m=>new Recall(key,m.Object("profile")?.Object("habits")?[property]?.ToString() ?? "",m.Str("id"))).Where(x=>x.SourceId.Length>0 && Valid(key,x.Value)).ToArray();
            if(values.Select(x=>x.Value).Distinct().Count()==1)return values[0];
        }
        return null;
    }
    private static bool Valid(string key,string value)=>key=="daily_steps" ? int.TryParse(value,out var steps) && steps is >=500 and <=60000 : value.Length is >0 and <=80 && PetQuestions.Known(value) && !value.Contains('\n');
    private static string WithNext(PetSession s,string text){s.Dialogue!.RequestedField="companion";return text+"\n"+KokoDialogue.StepQuestion(s);}
    public static JsonObject? Direct(PetSession s,string text,PetQuickReply? choice)
    {
        if(!Enabled(s))return null;
        var d=s.Dialogue!;var intent=choice?.Intent ?? "";
        if(text.StartsWith("关于「") && text.Contains("时间不够了")) {
            s.Fields["adjustmentNeed"]="endTime";s.Fields.Remove("adjustmentEndMinute");
            return Turn("别赶啦，我们把重要的留下。第几站先不急着删——这一天最晚几点结束？选个时间，也可以告诉我 HH:mm。",Q("14:00 结束","adjust_end_840"),Q("16:00 结束","adjust_end_960"),Q("18:00 结束","adjust_end_1080"),Q("我自己说时间","compose_idea"));
        }
        if(s.Fields.GetValueOrDefault("adjustmentNeed")=="endTime" || s.Fields.ContainsKey("adjustmentEndMinute") && (Regex.IsMatch(text,@"(?<!\d)([01]?\d|2[0-3])[:：]([0-5]\d)(?!\d)") || text is "先不改时间" or "取消调整")) {
            int? minute=null;
            if(intent.StartsWith("adjust_end_") && int.TryParse(intent[11..],out var chosen))minute=chosen;
            else {var clock=Regex.Match(text,@"(?<!\d)([01]?\d|2[0-3])[:：]([0-5]\d)(?!\d)");if(clock.Success)minute=int.Parse(clock.Groups[1].Value)*60+int.Parse(clock.Groups[2].Value);}
            if(minute is >=0 and <1440) {
                s.Fields["adjustmentEndMinute"]=minute.Value.ToString();s.Fields.Remove("adjustmentNeed");
                return Next("这一天按 "+$"{minute/60:00}:{minute%60:00}"+" 前结束来试算。先看建议，确认后才保存；其他日期和长期习惯保留。");
            }
            if(text is "先不改时间" or "取消调整") {s.Fields.Remove("adjustmentNeed");s.Fields.Remove("adjustmentEndMinute");return Next("好，结束时间沿用原安排，我们不赶着改。");}
            return Turn("我还没确定你想几点结束。告诉我例如 14:00，或者先不改时间，原行程都在。",Q("14:00 结束","adjust_end_840"),Q("16:00 结束","adjust_end_960"),Q("先不改时间","adjust_end_cancel"));
        }
        if(intent.StartsWith("mood_")) {
            d.MoodAnswered=true;
            var warmth=intent switch {
                "mood_rest"=>"那这次不用努力打卡啦。我们给坐着吹风和发呆留点位置。",
                "mood_food"=>"好呀，小馋雷达亮了！我们给好吃的留个位置，预算还是听你的。",
                "mood_explore"=>"那就带一点好奇心出发，给没见过的小惊喜留点空位。",
                _=>"好，我们直接聊旅行。你不用先整理好心情，想到什么说什么就好。"
            };
            if(intent=="mood_rest"){s.Fields["energy"]="低精力";s.Fields["rhythm"]="轻松随意";}
            if(intent=="mood_food")s.Fields["interests"]=string.Join("、",new[]{s.Fields.GetValueOrDefault("interests",""),"美食"}.Where(x=>x.Length>0).Distinct());
            if(intent=="mood_explore")s.Fields["notes"]=string.Join("；",new[]{s.Fields.GetValueOrDefault("notes",""),"本次想找点新鲜感"}.Where(x=>x.Length>0));
            var recall=intent=="mood_skip" ? null:CurrentRecall(s);
            if(!d.RecallOffered && recall is not null && KokoDialogue.NextNeed(s).Length>0) {
                d.RecallOffered=true;d.RecallKey=recall.Key;d.RecallValue=recall.Value;d.RecallSourceId=recall.SourceId;
                var habit=recall.Key=="daily_steps" ? "每天约 "+recall.Value+" 步":recall.Value;
                return Turn(warmth+"\n我记得你确认过「"+habit+"」。这次也参考它吗？想换换也可以。",Q("沿用这个习惯","recall_keep"),Q("这次想换换","recall_change"),Q("先说想去哪儿","recall_later"));
            }
            return Next(WithNext(s,warmth+"这份心情只放进这次的小背包。"));
        }
        if(intent.StartsWith("recall_")) {
            if(intent=="recall_later")return Next(WithNext(s,"好，先聊这次想去哪儿，习惯有需要再一起改。"));
            var current=CurrentRecall(s);
            if(current is null || current.Key!=d.RecallKey || current.Value!=d.RecallValue || current.SourceId!=d.RecallSourceId)
                return Next(WithNext(s,"刚刚那条习惯记录有变化，我们先按你现在的想法来。"));
            if(intent=="recall_change" && current.Key=="rhythm")return Turn("当然可以，你不用每次都一样。这次想慢一点，还是充实一点？只改这次的节奏。",Q("这次轻松点","trip_rhythm_relaxed"),Q("这次充实点","trip_rhythm_busy"),Q("我想自己说","compose_idea"));
            if(intent=="recall_change")return Turn("当然可以！你不用每次都一样。告诉我这次想走多少，或者选个大概范围，只改这次就好。",Q("少走些，约6000步","trip_steps_6000"),Q("多逛些，约12000步","trip_steps_12000"),Q("我想自己说","compose_idea"));
            s.Fields[current.Key=="daily_steps" ? "pace":"rhythm"]=current.Key=="daily_steps" ? "约"+current.Value+"步":current.Value;
            return Next(WithNext(s,"好，这次参考这个习惯。之后临时想换，也随时告诉我。"));
        }
        if(intent is "trip_rhythm_relaxed" or "trip_rhythm_busy") {
            s.Fields["rhythm"]=intent=="trip_rhythm_relaxed" ? "轻松随意":"紧凑充实";
            return Next(WithNext(s,"这次的节奏收好啦，原来的长期习惯保留。之后想再改，也没关系。"));
        }
        if(intent is "trip_steps_6000" or "trip_steps_12000") {
            s.Fields["pace"]="约"+intent[11..]+"步";
            return Next(WithNext(s,"这次的小步数收好啦。长期习惯不动，我们按这次舒服的范围来。"));
        }
        return null;
    }
    public static bool NeedsRest(string message)=>!Regex.IsMatch(message,@"\btired of\b",RegexOptions.IgnoreCase) && Regex.IsMatch(message,@"最近很累|有点累|好累|太累|想歇|想放松|\b(?:tired|exhausted|need (?:a )?rest|want (?:a )?rest|like a rest|need to relax)\b",RegexOptions.IgnoreCase) && !Regex.IsMatch(message,@"不累|没觉得累|并不累|不是很累|不觉得累|不想放松|不想歇|\b(?:not|never|don.?t feel|do not feel)\s+(?:very |really |at all )?(?:tired|exhausted)\b|\b(?:don.t|do not)\s+(?:want|need|feel like)\s+(?:to )?(?:a )?(?:rest|relax)\b",RegexOptions.IgnoreCase);
    public static bool TemporaryFeeling(string message)=>NeedsRest(message) || Regex.IsMatch(message,"这次.*心情|今天.*心情|最近.*疲惫|(?:this trip|today|lately).*(?:mood|feel|exhausted)",RegexOptions.IgnoreCase);
    public static void ApplyFeeling(PetSession s,string message) {
        if(Enabled(s) && NeedsRest(message)){s.Fields["energy"]="低精力";s.Fields["rhythm"]="轻松随意";}
    }
    public static PetMessage RespondWithCare(PetSession s,PetMessage reply,string message)
    {
        if(!Enabled(s))return reply;
        string prefix="";
        if(Regex.IsMatch(message,"改主意|不想去了|取消|换个|换一组") && !Regex.IsMatch(message,"不要取消|别取消|不取消|不要换|不想换|不用换"))
            prefix="改变主意也没关系，旅行又不是交作业。先一起调整想法，确认后再更新行程。";
        else if(NeedsRest(message) && !reply.Text.Contains("不用努力打卡"))
            prefix="那这次不用努力打卡啦。我们给休息和发呆留点位置。";
        return prefix.Length==0 || reply.Text.StartsWith(prefix) ? reply:reply with{Text=prefix+"\n"+reply.Text,Mood="relaxed"};
    }
}
