using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SydneyBuddy.Api;

public sealed record PetQuickReply(string Id,string Text,string Intent="respond");
public sealed record PetChoiceEvidence(string QuestionMessageId,string QuestionText,string OptionId,string OptionText);
public sealed record TravelInspiration(string Title,string[] Ideas,string Status="inspiration",bool PlacesVerified=false);

// Dialogue completion and long-term memory consent are deliberately independent.
public sealed class ProfilingDialogue
{
    public int Version { get; set; } = 2;
    public int CompanionVersion {get;set;}
    public bool MoodAnswered {get;set;}
    public bool RecallOffered {get;set;}
    public string RecallKey {get;set;}="";
    public string RecallValue {get;set;}="";
    public string RecallSourceId {get;set;}="";
    public int ResponseCount { get; set; }
    public string Phase { get; set; } = "introducing";
    public string Nickname { get; set; } = "";
    public string[] Ideas { get; set; } = [];
    public int IdeaRevision { get; set; }
    public string ProposedDirection { get; set; } = "";
    public string RequestedField {get;set;}="";
    public bool DateDeferred {get;set;}
    public string SelectedDirection { get; set; } = "";
    public TravelInspiration? Inspiration { get; set; }
}

public static class KokoDialogue
{
    public const int MaximumResponses=4;
    public const string Prompt="""
你叫 Koko，是亲切、主动、靠谱、有分寸的考拉旅行向导。dialogue 是首次画像进度，currentChoice 是用户对上一提议的点选，绝非孤立的原话。
初见的可选称呼由程序处理；已知昵称自然使用，别重新介绍自己或要求重填。用户姓名不能从示例、资料或推测获取。
用旅前视角共同构想：这趟、到时候、如果路线上有、咱们可以先……。通常1–2句，只做一个当前决定，主动贡献具体体验，接上用户的选择。用户拒绝就换想法，不说服、不要求理由。别每轮重复“好呀”、复述、卖萌、夸奖；别虚构亲身经历或看过作品。
JSON 在原协议上增加 quickReplies:[{"text":"短自然回复","intent":"respond 或 skip 或 draft"}]、ideas:["逐渐形成的具体玩法设想"]、readyForDraft:bool。
初聊每轮通常3个可点击回复，只有一个决定，包含拒绝/换想法/跳过的出口，不预选或标推荐。按钮本身就能完成回答，不能强制打字。兴趣入口有跳过出口，不能困在换一组的循环里。问题顺序根据已有信息变化，不能固定遍历字段或十二维度。
剩余初聊轮数由 dialogue.responsesRemaining 给出，含本轮回答。mustDraft=true 或用户说快点、别问了、直接安排时，立即给已有信息下的灵感草案，reply 不再追加画像问题，readyForDraft=true。信息已足够时可以提前结束。称呼也计入最多4轮；偏好确认只用可选卡片。阶段成果应让人看见之前的选择怎样改变了方案。
目的地、日期、预算、人数未知不妨碍灵感；仍保持未知，不默认悉尼/一天/一人/预算或已预订。灵感不是正式时间表、报价或预订。此对话没有地点核验工具，ideas 只能是明确有条件的玩法设想，不能声称某个场所存在、已营业、某价格/班次真实或天气已核验；需要时说之后核实。
ideas 是 Koko 的提议，不是用户偏好。currentChoice 的点击只适用于 questionText 中这个场景；提取时 scope=activity，condition 必须保留完整适用活动和条件，evidence 必须是 optionText 原文。不能把“顺路再去”推断成专程参观或购买；不能把慢慢吃一餐推断为整趟慢节奏。
明确日常习惯可成为 long_term 候选，本次要求用 trip，特定活动用 activity；AI 猜测仍用 inferred。所有长期候选等可选记忆卡确认，不能因点击/高可信度或看到模拟同意就声称已保存。
推断边界：宅家/动漫不代表体力低或内向、看过某作品；餐厅等位20–30分钟不是精确上限/所有队伍；为值得的景色早起一次不代表日常作息；傍晚海港避晒不是怕热/怕冷；接受散步不代表步行上限/爬坡能力；公交轻轨可用不代表换乘上限/打车预算；未报告忌口不等于已排除过敏。没有证据的相关维度保持未知，别放进 fields 绕过偏好证据限制。
nickname、ideas、quickReplies 都不能来自产品文档里的模拟用户。不要预填任何模拟偏好。进入 inspiration 后继续围绕草案修改或回答，不重启必答画像；正式规划需要的事实由统一资料入口补。
""";

    public static void Enable(PetSession session,JsonObject input)
    {
        if(input.Int("profilingVersion")!=2)return;
        if(session.Dialogue is not null){if(input.Int("companionVersion")==1)session.Dialogue.CompanionVersion=1;return;}
        var name=input.Str("nickname").Trim();
        if(name.Length>24 || name.Any(char.IsControl))name="";
        var previousResponses=session.Messages.Count(x=>x.Role=="user");
        session.Dialogue=new(){Nickname=name,ResponseCount=Math.Min(previousResponses,MaximumResponses),CompanionVersion=input.Int("companionVersion")==1 ? 1:0};
        if(previousResponses>0) {
            session.Dialogue.Phase="exploring";
            if(previousResponses>=MaximumResponses)Finish(session,[]);
            // Upgrading a saved draft preserves every prior answer and never repeats the introduction.
            session.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",
                session.Dialogue.Inspiration is null ? "咱们接着已有的想法准备。这趟先留一段自由探索，还是围绕一个最想体验的事展开？":"先把已有想法整理成灵感方向，细节可以之后一起补。",
                "curious",session.Dialogue.Inspiration is null ? ExplorationReplies():DraftReplies()));
            return;
        }
        var returning=input.Bool("hasMetKoko") || name.Length>0;
        if(returning)session.Dialogue.Phase="exploring";
        // A legacy empty draft only contains the old welcome. Replace it rather than introducing twice.
        if(session.Messages.All(x=>x.Role=="assistant"))session.Messages.Clear();
        var greeting=returning
            ? (name.Length>0 ? $"{name}，咱们先挑一种玩法：绿意慢走，还是城市故事？你也可以自己说。":"咱们先挑一种玩法：绿意慢走，还是城市故事？你也可以自己说。")
            : "嗨，我叫 Koko，是你的旅行向导。怎么称呼你？也可以先跳过。";
        session.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",KokoCompanion.Enabled(session) && returning ? KokoCompanion.Greeting:greeting,"greeting",KokoCompanion.Enabled(session) && returning ? KokoCompanion.MoodReplies():returning ? ExplorationReplies():NameReplies()));
    }

    public static bool RepeatedChoice(PetSession s,JsonObject input) {
        if(input.Str("field").Length>0 || input.Str("questionId").Length==0 || input.Str("optionId").Length==0)return false;
        var previous=s.Messages.LastOrDefault(x=>x.Role=="user" && x.Choice?.QuestionMessageId==input.Str("questionId") && x.Choice.OptionId==input.Str("optionId"));
        return previous?.Choice is {} choice && (input.Str("message").Trim()==choice.OptionText || s.ResponseLanguage=="en" && input.Str("message").Trim()==PetEnglishPresenter.Display(s,choice.OptionText));
    }
    public static bool DateIsDeferred(PetSession s) {
        if(s.Dialogue is not {} d || PetQuestions.Answered(s,"date") && PetQuestions.Answered(s,"days"))return false;
        return d.DateDeferred || d.RequestedField=="time" || s.Messages.Any(x=>x.Role=="user" && (x.Choice?.OptionText=="日期还没定" || x.Text is "日期还没定" or "Date still open"));
    }
    // Repair old drafts' available actions without appending conversation or touching facts.
    public static void RefreshDeferredState(PetSession s) {
        if(s.Dialogue is not {} d)return;
        d.DateDeferred=DateIsDeferred(s);
        var last=s.Messages.FindLastIndex(x=>x.Role=="assistant");
        if(d.DateDeferred && last>=0 && s.Messages[last].QuickReplies?.Any(x=>x.Intent=="defer_time")==true){
            var options=s.Messages[last].QuickReplies!.Where(x=>x.Intent!="defer_time").ToArray();
            s.Messages[last]=s.Messages[last] with {QuickReplies=options};
        }
    }
    public static PetQuickReply? ResolveChoice(PetSession s,JsonObject input)
    {
        s.CurrentChoice=null;
        if(input.Str("optionId").Length==0 && input.Str("questionId").Length==0)return null;
        var question=s.Messages.LastOrDefault(x=>x.Role=="assistant");
        var option=question?.QuickReplies?.FirstOrDefault(x=>x.Id==input.Str("optionId"));
        if(s.Dialogue is null || question?.Id!=input.Str("questionId") || option is null || option.Intent is "compose_name" or "details")
            throw new ServiceException("这组选项已更新，请使用当前回复，或直接告诉 Koko。 ");
        if(input.Str("field").Length>0 || (input.Str("message").Trim()!=option.Text && (s.ResponseLanguage!="en" || input.Str("message").Trim()!=PetEnglishPresenter.Display(s,option.Text))))
            throw new ServiceException("点选内容与当前提议不一致，请刷新后重试。");
        s.CurrentChoice=new(question!.Id,question.Text,option.Id,option.Text);
        return option;
    }

    public static bool Expedite(string message)=>new[]{"快点","别问了","不要再问","直接安排","直接给我草案","先看草案"}.Any(message.Contains);

    // Server-owned shortcuts never require a model call or a name to proceed.
    public static JsonObject? DirectResponse(PetSession s,string text,PetQuickReply? choice)
    {
        if(s.Dialogue is not {} d)return null;
        if(KokoCompanion.Direct(s,text,choice) is {} companion)return companion;
        if(choice?.Intent=="choose_sydney") {s.Fields["destination"]="悉尼";return StepResult(s,"那就去悉尼，其他资料都留着。", "destination");}
        if(choice?.Intent is "people_1" or "people_2") {s.Fields["travelers"]=choice.Intent[^1..];return StepResult(s,"同行人数收好啦。", "travelers");}
        if(choice?.Intent is "budget_60" or "budget_120") {
            s.Fields["budget"]=choice.Intent[7..];s.Fields["budgetCurrency"]="AUD";s.Fields["budgetBasis"]="perPersonPerDay";s.Fields["budgetCoverage"]="activities";d.RequestedField="";
            return StepResult(s,"这次按每人每天 "+s.Fields["budget"]+" AUD 活动预算来安排，不含住宿、交通和全部餐饮。", "budget");
        }
        if(choice?.Intent=="defer_time") {s.Fields.Remove("date");d.DateDeferred=true;d.RequestedField="";return StepResult(s,"日期先留空，我不会替你确定。草稿会保留，准备生成时再选日期。", "time");}
        if(BudgetQuestion(text)) {
            s.Fields.Remove("budget");d.RequestedField="budget";
            return new(){["reply"]="原预算先不采用。可以先选每人每天 60 或 120 AUD 作为活动预算，再自由调整；这只是预算档位，不代表已核实的花费。住宿、交通和全部餐饮另计。",["readyForDraft"]=true,["followTripStep"]=true};
        }
        if(choice?.Intent=="change_direction" || text is "换个思路吧" or "换个想法吧" or "换一组想法") {
            var current=Array.IndexOf(DirectionKeys,d.ProposedDirection);
            return Propose(s,DirectionKeys[(current+1+DirectionKeys.Length)%DirectionKeys.Length],false);
        }
        if(choice?.Intent is "direction_nature" or "direction_culture" or "direction_food")return Propose(s,choice.Intent[10..],true);
        if(choice?.Intent=="accept_direction")return Propose(s,d.ProposedDirection.Length>0 ? d.ProposedDirection:DefaultDirection(s),true);
        if(choice?.Intent=="keep")return d.ProposedDirection.Length>0 ? Propose(s,d.ProposedDirection,true):Propose(s,DefaultDirection(s),false);
        if(choice?.Intent=="draft" || Expedite(text)) {
            if(d.Ideas.Length==0){var proposed=Propose(s,DefaultDirection(s),false);proposed["reply"]="先给你一版「"+DirectionTitle(d.ProposedDirection)+"」方向，具体资料可以接着补。";return proposed;}
            return new(){["reply"]="先用已有的线索构想一版，未知的资料可以之后一起补。",["readyForDraft"]=true};
        }
        if(text=="留一段自由探索")return Propose(s,"nature",false);
        var match=Regex.Match(text,@"^(?:叫我|我叫|称呼我)\s*([^,，。!！？\r\n]{1,24})[。！!]?$");
        // A single name in direct response to the optional name question is explicit evidence.
        var shortName=d.Phase=="introducing" && choice is null && Regex.IsMatch(text,@"^[A-Za-z][A-Za-z'’-]{0,23}$") &&
            !new[]{"skip","hello","hi","yes","no","unknown"}.Contains(text,StringComparer.OrdinalIgnoreCase);
        if(choice?.Intent=="use_alias")d.Nickname="旅行搭子";
        else if(match.Success)d.Nickname=match.Groups[1].Value.Trim();
        else if(shortName)d.Nickname=text;
        if(d.Phase!="introducing")return null;
        if(choice?.Intent is not ("skip_name" or "use_alias") && !match.Success && !shortName)return null;
        if(KokoCompanion.Enabled(s))return new(){["reply"]=KokoCompanion.Greeting,["companionTurn"]=true,["companionReplies"]=System.Text.Json.JsonSerializer.SerializeToNode(KokoCompanion.MoodReplies(),JsonExtensions.Options)};
        return new(){["reply"]=(d.Nickname.Length>0 ? $"{d.Nickname}，咱们先找一点这趟旅行的灵感。":"咱们先找一点这趟旅行的灵感。")+"先挑一种玩法：绿意慢走，还是城市故事？也可以换一组。",
            ["offerDirections"]=true};
    }

    public static PetMessage CompleteTurn(PetSession s,JsonObject result,string reply,string mood)
    {
        if(s.Dialogue is not {} d)return new(Guid.NewGuid().ToString("N"),"assistant",reply,mood);
        if(s.PersonalizingTrip && !result.Bool("followTripStep") && !result.Bool("companionTurn")){
            var optional=(result.Array("quickReplies") ?? new JsonArray()).OfType<JsonObject>().Where(x=>x.Str("text").Length is >0 and <=45).Take(3).Select(x=>Reply(x.Str("text"))).ToArray();
            return new(Guid.NewGuid().ToString("N"),"assistant",reply,mood,optional);
        }
        if(result.Bool("companionTurn")){d.ResponseCount=Math.Min(MaximumResponses,d.ResponseCount+1);d.Phase="exploring";return new(Guid.NewGuid().ToString("N"),"assistant",reply,"relaxed",KokoCompanion.Options(result));}
        var initial=d.Inspiration is null;
        if(initial)d.ResponseCount=Math.Min(MaximumResponses,d.ResponseCount+1);
        var ideas=(result.Array("ideas") ?? new JsonArray()).OfType<JsonValue>()
            .Select(x=>x.TryGetValue<string>(out var t) ? t.Trim():"").Where(x=>x.Length is >0 and <=240).Distinct().Take(3).ToArray();
        if(ideas.Length>0)d.Ideas=ideas;
        var hasTripFacts=s.Fields.Any(x=>PetQuestions.Required.Contains(x.Key) && PetQuestions.Known(x.Value));
        var finish=d.Inspiration is not null || d.ResponseCount>=MaximumResponses || result.Bool("readyForDraft") || hasTripFacts;
        PetQuickReply[] options;
        if(finish) {
            Finish(s,d.Ideas);
            // The fourth answer ends the initial interview even when the model asks another question.
            if(initial && !result.Bool("directionChanged") && !result.Bool("followTripStep")) {
                if(hasTripFacts){if(string.IsNullOrWhiteSpace(reply) || reply.Contains('？') || reply.Contains('?'))reply="这次的旅行资料先收好啦。";reply+="\n"+StepQuestion(s);}
                else reply="先把这趟的方向摆出来了。下面是可以一起调整的灵感，具体场所、时间和费用等资料齐了再核实。";
            }
            options=DraftReplies(s);
        } else {
            d.Phase="exploring";
            var proposed=(result.Array("quickReplies") ?? new JsonArray()).OfType<JsonObject>().Take(4)
                .Where(x=>x.Str("text").Length is >0 and <=45)
                .Select(x=>new PetQuickReply(Guid.NewGuid().ToString("N"),x.Str("text"),x.Str("intent") is "draft" or "skip" ? x.Str("intent"):"respond"))
                .DistinctBy(x=>x.Text).ToArray();
            if(result.Bool("offerDirections"))options=ExplorationReplies();
            else if(proposed.Length is >=3 and <=4 && proposed.Any(x=>x.Intent is "skip" or "draft"))options=proposed;
            else {
                // Safe generic reactions bind to the actual proposal; no unrelated field questionnaire.
                options=[Reply("就按这个想法试试"),Reply("换个想法吧","skip"),Reply("先看已有的草案","draft")];
                if(string.IsNullOrWhiteSpace(reply))reply="咱们先留一段可以随心逛逛的时间，再围绕你最想体验的事串起来？";
            }
        }
        if(result.Bool("followTripStep")){reply=result.Str("reply");options=DraftReplies(s);}
        else if(finish && result.Bool("directionChanged") && d.SelectedDirection==d.ProposedDirection)reply+="\n"+StepQuestion(s);
        return new(Guid.NewGuid().ToString("N"),"assistant",reply,mood,options);
    }

    private static void Finish(PetSession s,string[] ideas)
    {
        var d=s.Dialogue!;d.Phase="inspiration";
        if(ideas.Length==0){d.ProposedDirection=DefaultDirection(s);ideas=DirectionIdeas(s,d.ProposedDirection);}
        d.Ideas=ideas;
        d.Inspiration=new(PetQuestions.Known(s.Fields.GetValueOrDefault("destination")) ? s.Fields["destination"]+" · "+DirectionTitle(d.ProposedDirection):"目的地待定 · "+DirectionTitle(d.ProposedDirection),ideas);
    }
    private static PetQuickReply Reply(string text,string intent="respond")=>new(Guid.NewGuid().ToString("N"),text,intent);
    private static PetQuickReply[] NameReplies()=>[Reply("先跳过称呼","skip_name"),Reply("叫我旅行搭子","use_alias"),Reply("我来写个称呼","compose_name")];
    private static PetQuickReply[] ExplorationReplies()=>[Reply("绿意慢走","direction_nature"),Reply("城市故事","direction_culture"),Reply("换一组想法","change_direction")];
    public static bool BudgetQuestion(string message)=>message.Contains("预算") && !Regex.IsMatch(message,@"\d") && Regex.IsMatch(message,"没想好|不确定|大概范围|推荐|建议");
    public static string NextNeed(PetSession s) {
        if(s.Dialogue?.RequestedField=="budget" && !PetQuestions.Answered(s,"budget"))return "budget";
        if(!PetQuestions.Answered(s,"destination"))return "destination";
        if(!DateIsDeferred(s) && (!PetQuestions.Answered(s,"date") || !PetQuestions.Answered(s,"days")))return "time";
        if(!PetQuestions.Answered(s,"travelers"))return "travelers";
        if(!PetQuestions.Answered(s,"budget") || !PetQuestions.Answered(s,"budgetCurrency") || !PetQuestions.Answered(s,"budgetBasis"))return "budget";
        if(!PetQuestions.Answered(s,"date") || !PetQuestions.Answered(s,"days"))return "time";
        return "";
    }
    public static string StepQuestion(PetSession s)=>NextNeed(s) switch {
        "destination"=>PetQuestions.UnsupportedDestination(s) ? "资料会保留。目前只支持悉尼路线，可以改选悉尼或先保存草稿。":"这次想去哪个城市？目前可以生成悉尼路线，也能先保存其他城市的想法。",
        "time"=>DateIsDeferred(s) ? "其他资料都收好啦。日期继续留空，准备生成时再选日期和天数。":"接下来选出发日期和天数，最多 15 天；没定日期也可以先保留草稿。",
        "travelers"=>"这次一共几个人？记得包括自己呀。",
        "budget"=>"活动预算可以选 60 / 120 AUD 每人每天，或自定义金额与范围；住宿、交通和全部餐饮另计。",
        _=>"资料准备齐啦！可以直接生成行程，也可以继续聊或单独修改。"
    };
    private static JsonObject StepResult(PetSession s,string text,string focus)=>new(){["reply"]=text+"\n"+StepQuestion(s),["readyForDraft"]=true,["followTripStep"]=true};
    public static bool AcceptInspiration(PetSession s,string direction) {
        if(s.Dialogue is not {} d || !DirectionKeys.Contains(direction))throw new ServiceException("Please choose a current travel idea.");
        var added=direction switch {"culture"=>new[]{"文化","艺术"},"food"=>new[]{"美食","城市"},_=>new[]{"自然","风景"}};
        var current=s.Fields.GetValueOrDefault("interests","");
        var interests=(PetQuestions.Known(current) ? current.Split(['、',',','，',';','；'],StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries):[]).ToList();
        if(d.SelectedDirection==direction && added.All(interests.Contains))return false;
        s.Fields["interests"]=string.Join("、",interests.Concat(added).Distinct());
        d.SelectedDirection=direction;d.ProposedDirection=direction;d.IdeaRevision++;
        Finish(s,DirectionIdeas(s,direction));
        var title=direction switch {"culture"=>"A little culture, a little wandering.","food"=>"Good food, without the rush.",_=>"A little greenery, a slower day."};
        var reply=s.ResponseLanguage=="en" ? "Added to this trip: "+title+" I’ll keep your other interests too. You can generate the itinerary or keep chatting.":"这个灵感加入这次旅行啦，之前的兴趣也留着。可以生成行程，也可以继续聊。";
        s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",reply,"excited"));
        if(s.Messages.Count>100)s.Messages=s.Messages.TakeLast(100).ToList();
        return true;
    }
    public static void DetailsUpdated(PetSession s,JsonObject changes) {
        if(s.Dialogue is null || !changes.Any(x=>PetQuestions.Required.Contains(x.Key) || new[]{"budgetCoverage","origin","mustDo","avoid","companionNeeds"}.Contains(x.Key)))return;
        if(changes.ContainsKey("date"))s.Dialogue.DateDeferred=!PetQuestions.Answered(s,"date");
        if(PetQuestions.Answered(s,"budget"))s.Dialogue.RequestedField="";
        if(s.Dialogue.Inspiration is null)Finish(s,[]);
        var labels=new Dictionary<string,string>{{"destination","目的地"},{"date","日期"},{"days","天数"},{"travelers","人数"},{"budget","预算"},{"origin","出发地点"},{"mustDo","必去安排"},{"avoid","避开安排"},{"companionNeeds","同行需要"}};
        var names=string.Join("、",labels.Where(x=>changes.ContainsKey(x.Key)).Select(x=>x.Value));
        var text=(names.Length>0 ? names+"收好啦。":"旅行资料已更新。")+"\n"+StepQuestion(s);
        s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",text,"curious",DraftReplies(s)));
        if(s.Messages.Count>100)s.Messages=s.Messages.TakeLast(100).ToList();
    }
    private static PetQuickReply[] DraftReplies(PetSession? s=null) {
        if(s is null)return [Reply("补充出行资料","details"),Reply("再换个玩法","change_direction"),Reply("我想自己说","compose_idea")];
        if(s.Dialogue is {} d && d.ProposedDirection.Length>0 && d.SelectedDirection!=d.ProposedDirection && d.RequestedField.Length==0 && !s.Fields.Keys.Any(PetQuestions.Required.Contains))return [Reply("就选这个方向","accept_direction"),Reply("换个玩法","change_direction"),Reply("我想自己说","compose_idea")];
        return NextNeed(s) switch {
            "destination"=>PetQuestions.UnsupportedDestination(s) ? [Reply("改选悉尼","choose_sydney"),Reply("修改城市","details_destination"),Reply("保留草稿","keep_draft")]:[Reply("选悉尼","choose_sydney"),Reply("其他城市","details_destination"),Reply("我想自己说","compose_idea")],
            "time"=>DateIsDeferred(s) ? [Reply("选日期再生成","details_time"),Reply("保留草稿","keep_draft"),Reply("我想自己说","compose_idea")]:[Reply("选日期和天数","details_time"),Reply("日期还没定","defer_time"),Reply("我想自己说","compose_idea")],
            "travelers"=>[Reply("1 人","people_1"),Reply("2 人","people_2"),Reply("其他人数","details_people")],
            "budget"=>[Reply("60 AUD / 人 / 天","budget_60"),Reply("120 AUD / 人 / 天","budget_120"),Reply("自定义预算","details_budget")],
            _=>[Reply("修改旅行资料","details"),Reply("再换个玩法","change_direction"),Reply("我想自己说","compose_idea")]
        };
    }
    private static readonly string[] DirectionKeys=["nature","culture","food"];
    private static string DefaultDirection(PetSession s)=>s.Fields.GetValueOrDefault("interests","").Contains("文化") || s.Fields.GetValueOrDefault("interests","").Contains("艺术") ? "culture":s.Fields.GetValueOrDefault("interests","").Contains("美食") ? "food":"nature";
    private static string DirectionTitle(string key)=>key switch {"nature"=>"绿意慢走","culture"=>"城市故事","food"=>"街区逛吃",_=>"旅行灵感"};
    private static string[] DirectionIdeas(PetSession s,string key)=>key switch {
        "culture"=>["先逛一段有故事的街区，看看建筑和当地生活；实际街区之后核实。",s.Fields.GetValueOrDefault("avoid","").Contains("博物馆") ? "避开博物馆，把另一段时间留给街景、小店和休息。":"如果有合适的展览或文化场所，挑一处慢慢看，再留一段休息；开放时间待核实。"],
        "food"=>["挑一个餐饮集中的街区边逛边吃，具体店铺、营业和价格之后核实。","把用餐和附近的小店串在一起，中间留休息，不为打卡来回赶路。"],
        _=>["如果目的地有合适的公园、绿道或海边，先留一段轻松散步；具体地点之后核实。","走累了就找个方便休息的地方，下午留给附近的街景，不把一天排满。"]};
    private static JsonObject Propose(PetSession s,string key,bool accepted) {
        var d=s.Dialogue!;d.ProposedDirection=key;d.IdeaRevision++;d.Ideas=DirectionIdeas(s,key);
        if(accepted){d.SelectedDirection=key;s.Fields["interests"]=key switch {"culture"=>"文化、艺术","food"=>"美食、城市",_=>"自然、风景"};}
        return new(){["reply"]=accepted ? "那这次就按「"+DirectionTitle(key)+"」来准备。这个玩法只用于这次旅行。":"换个具体方向：「"+DirectionTitle(key)+"」。"+d.Ideas[0]+"这个玩法怎么样？",["ideas"]=new JsonArray(d.Ideas.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["readyForDraft"]=true,["directionChanged"]=true};
    }
}
