using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SydneyBuddy.Api;

// Opt-in iOS companion experience. Feelings stay in trip fields; this never writes memory.
public static class KokoCompanion
{
    public const string Prompt="""
Acknowledge feelings the user explicitly expresses, then help with the trip. For tiredness, suggest rest and breathing room without inferring a medical diagnosis or long-term fitness. Wanting good food does not mean increasing the budget. Declining, cancelling or changing one's mind needs no reason; do not express disappointment, persuade or apply guilt or streak pressure.
Refer only to currently valid confirmed memories, retaining their exact conditions and allowing this trip to be different. Current feelings and temporary changes belong only in this trip's fields, never the long-term profile. Long-term changes still require the optional confirmation card.
Be kind in a concrete, concise way, with an occasional little food enthusiasm or gentle humour, without repeated catchphrases. Never claim to have changed a saved itinerary, stored a memory or verified a place unless the corresponding operation actually succeeded. When enough is known, proceed to generation without extra questions about feelings or memories.
""";
    public static bool Enabled(PetSession s)=>s.Dialogue?.CompanionVersion==1;
    public static PetQuickReply[] MoodReplies()=>[Q("I'd like a rest","mood_rest"),Q("I'd love some good food","mood_food"),Q("Something new sounds good","mood_explore"),Q("Let's talk about the trip","mood_skip")];
    public const string Greeting="My backpack's ready. Are you after a little rest, some good food or something new? You can also tell me where you'd like to go.";
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
        if((text.StartsWith("关于「") && text.Contains("时间不够了")) || (text.StartsWith("About",StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(text,"not enough time|short on time",RegexOptions.IgnoreCase))) {
            s.Fields["adjustmentNeed"]="endTime";s.Fields.Remove("adjustmentEndMinute");
            return Turn("Let's keep what matters without rushing. Before we remove any stops, what time does this day need to finish? Pick a time, or tell me in HH:mm.",Q("Finish at 14:00","adjust_end_840"),Q("Finish at 16:00","adjust_end_960"),Q("Finish at 18:00","adjust_end_1080"),Q("I'll enter a time","compose_idea"));
        }
        if(s.Fields.GetValueOrDefault("adjustmentNeed")=="endTime" || s.Fields.ContainsKey("adjustmentEndMinute") && (Regex.IsMatch(text,@"(?<!\d)([01]?\d|2[0-3])[:：]([0-5]\d)(?!\d)") || IsCancel(text,intent))) {
            int? minute=null;
            if(intent.StartsWith("adjust_end_") && int.TryParse(intent[11..],out var chosen))minute=chosen;
            else {var clock=Regex.Match(text,@"(?<!\d)([01]?\d|2[0-3])[:：]([0-5]\d)(?!\d)");if(clock.Success)minute=int.Parse(clock.Groups[1].Value)*60+int.Parse(clock.Groups[2].Value);}
            if(minute is >=0 and <1440) {
                s.Fields["adjustmentEndMinute"]=minute.Value.ToString();s.Fields.Remove("adjustmentNeed");
                return Next("I'll work towards a "+$"{minute/60:00}:{minute%60:00}"+" finish for this day. You'll see suggestions before confirming any saved changes; other days and long-term habits stay as they are.");
            }
            if(IsCancel(text,intent)) {s.Fields.Remove("adjustmentNeed");s.Fields.Remove("adjustmentEndMinute");return Next("We'll keep the original finish time. There's no rush to change it.");}
            return Turn("What finish time would you like, for example 14:00? You can also keep the original time; your itinerary is still there.",Q("Finish at 14:00","adjust_end_840"),Q("Finish at 16:00","adjust_end_960"),Q("Keep the original time","adjust_end_cancel"));
        }
        if(intent.StartsWith("mood_")) {
            d.MoodAnswered=true;
            var warmth=intent switch {
                "mood_rest"=>"There's no need to tick everything off. Let's leave room to sit, enjoy the breeze and do very little.",
                "mood_food"=>"My food radar is on! Let's make room for something delicious, with the budget still up to you.",
                "mood_explore"=>"Let's take a little curiosity along and leave space for a few new discoveries.",
                _=>"Let's talk about the trip. You don't need to have everything figured out; say whatever comes to mind."
            };
            if(intent=="mood_rest"){s.Fields["energy"]="低精力";s.Fields["rhythm"]="轻松随意";}
            if(intent=="mood_food")s.Fields["interests"]=string.Join("、",new[]{s.Fields.GetValueOrDefault("interests",""),"美食"}.Where(x=>x.Length>0).Distinct());
            if(intent=="mood_explore")s.Fields["notes"]=string.Join("；",new[]{s.Fields.GetValueOrDefault("notes",""),"本次想找点新鲜感"}.Where(x=>x.Length>0));
            var recall=intent=="mood_skip" ? null:CurrentRecall(s);
            if(!d.RecallOffered && recall is not null && KokoDialogue.NextNeed(s).Length>0) {
                d.RecallOffered=true;d.RecallKey=recall.Key;d.RecallValue=recall.Value;d.RecallSourceId=recall.SourceId;
                var habit=recall.Key=="daily_steps" ? "about "+recall.Value+" steps a day":HabitLabel(recall.Value);
                return Turn(warmth+"\nYou previously confirmed "+habit+". Shall we use that for this trip too? It's fine to change it.",Q("Use this habit","recall_keep"),Q("Change it for this trip","recall_change"),Q("Talk about the destination","recall_later"));
            }
            return Next(WithNext(s,warmth+" I'll use this only for this trip."));
        }
        if(intent.StartsWith("recall_")) {
            if(intent=="recall_later")return Next(WithNext(s,"Let's start with where you'd like to go. We can revisit your habits if we need to."));
            var current=CurrentRecall(s);
            if(current is null || current.Key!=d.RecallKey || current.Value!=d.RecallValue || current.SourceId!=d.RecallSourceId)
                return Next(WithNext(s,"That saved habit has changed. Let's go with what you want now."));
            if(intent=="recall_change" && current.Key=="rhythm")return Turn("Of course; every trip can be different. Would you like a gentler pace or a fuller day? This changes this trip's pace only.",Q("A gentler pace this trip","trip_rhythm_relaxed"),Q("A fuller day this trip","trip_rhythm_busy"),Q("I'll write my own","compose_idea"));
            if(intent=="recall_change")return Turn("Of course; every trip can be different. How much walking would feel right this time? Choose a rough range or tell me your own.",Q("Less walking, about 6,000 steps","trip_steps_6000"),Q("More exploring, about 12,000 steps","trip_steps_12000"),Q("I'll write my own","compose_idea"));
            s.Fields[current.Key=="daily_steps" ? "pace":"rhythm"]=current.Key=="daily_steps" ? "约"+current.Value+"步":current.Value;
            return Next(WithNext(s,"We'll use that habit for this trip. Tell me any time if you feel like changing it."));
        }
        if(intent is "trip_rhythm_relaxed" or "trip_rhythm_busy") {
            s.Fields["rhythm"]=intent=="trip_rhythm_relaxed" ? "轻松随意":"紧凑充实";
            return Next(WithNext(s,"I've noted this trip's pace and kept your long-term habit. You can change this trip again whenever you like."));
        }
        if(intent is "trip_steps_6000" or "trip_steps_12000") {
            s.Fields["pace"]="约"+intent[11..]+"步";
            return Next(WithNext(s,"I've noted the walking range for this trip. Your long-term habit stays the same; we'll use what feels comfortable this time."));
        }
        return null;
    }
    private static bool IsCancel(string text,string intent)=>intent=="adjust_end_cancel" || text is "先不改时间" or "取消调整" || text.Equals("Keep the original time",StringComparison.OrdinalIgnoreCase) || text.Equals("Cancel the change",StringComparison.OrdinalIgnoreCase);
    private static string HabitLabel(string value)=>value switch {"轻松随意"=>"a relaxed pace","适中"=>"a balanced pace","紧凑充实"=>"a full, active day",_=>value};
    public static bool NeedsRest(string message)=>Regex.IsMatch(message,@"最近很累|有点累|好累|太累|想歇|想放松|\b(?:tired|exhausted|need (?:a )?rest|want (?:a )?rest|like a rest|need to relax)\b",RegexOptions.IgnoreCase) && !Regex.IsMatch(message,@"不累|没觉得累|并不累|不是很累|不觉得累|不想放松|不想歇|\b(?:not|never|don.t feel)\s+(?:very |really |at all )?(?:tired|exhausted)\b|\b(?:don.t|do not)\s+(?:want|need|feel like)\s+(?:to )?(?:a )?(?:rest|relax)\b",RegexOptions.IgnoreCase);
    public static bool TemporaryFeeling(string message)=>NeedsRest(message) || Regex.IsMatch(message,"这次.*心情|今天.*心情|最近.*疲惫|(?:this trip|today|lately).*(?:mood|feel|exhausted)",RegexOptions.IgnoreCase);
    public static void ApplyFeeling(PetSession s,string message) {
        if(Enabled(s) && NeedsRest(message)){s.Fields["energy"]="低精力";s.Fields["rhythm"]="轻松随意";}
    }
    public static PetMessage RespondWithCare(PetSession s,PetMessage reply,string message)
    {
        if(!Enabled(s))return reply;
        string prefix="";
        if(Regex.IsMatch(message,@"改主意|不想去了|取消|换个|换一组|changed my mind|change my mind|\bcancel\b|try another|different idea",RegexOptions.IgnoreCase) && !Regex.IsMatch(message,@"不要取消|别取消|不取消|不要换|不想换|不用换|\b(?:don.t|do not|never) (?:want to )?(?:cancel|change)\b",RegexOptions.IgnoreCase))
            prefix="Changing your mind is fine; a trip isn't homework. Let's adjust the idea together, then update the itinerary after you confirm.";
        else if(NeedsRest(message) && !reply.Text.Contains("no need to tick everything off"))
            prefix="There's no need to tick everything off. Let's leave room to rest and do very little.";
        return prefix.Length==0 || reply.Text.StartsWith(prefix) ? reply:reply with{Text=prefix+"\n"+reply.Text,Mood="relaxed"};
    }
}
