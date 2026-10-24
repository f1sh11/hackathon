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
    public int ResponseCount { get; set; }
    public string Phase { get; set; } = "introducing";
    public string Nickname { get; set; } = "";
    public string[] Ideas { get; set; } = [];
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
        if(input.Int("profilingVersion")!=2 || session.Dialogue is not null)return;
        var name=input.Str("nickname").Trim();
        if(name.Length>24 || name.Any(char.IsControl))name="";
        var previousResponses=session.Messages.Count(x=>x.Role=="user");
        session.Dialogue=new(){Nickname=name,ResponseCount=Math.Min(previousResponses,MaximumResponses)};
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
        var greeting=returning
            ? (name.Length>0 ? $"{name}，咱们开始构想这趟旅行吧。先留一段自由探索，还是围绕一个最想体验的事展开？":"咱们开始构想新一趟旅行吧。先留一段自由探索，还是围绕一个最想体验的事展开？")
            : "嗨，我叫 Koko，是你的旅行向导。怎么称呼你？也可以先跳过。";
        session.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",greeting,"greeting",returning ? ExplorationReplies():NameReplies()));
    }

    public static PetQuickReply? ResolveChoice(PetSession s,JsonObject input)
    {
        s.CurrentChoice=null;
        if(input.Str("optionId").Length==0 && input.Str("questionId").Length==0)return null;
        var question=s.Messages.LastOrDefault(x=>x.Role=="assistant");
        var option=question?.QuickReplies?.FirstOrDefault(x=>x.Id==input.Str("optionId"));
        if(s.Dialogue is null || question?.Id!=input.Str("questionId") || option is null || option.Intent is "compose_name" or "details")
            throw new ServiceException("这组选项已更新，请使用当前回复，或直接告诉 Koko。 ");
        if(input.Str("field").Length>0 || input.Str("message").Trim()!=option.Text)
            throw new ServiceException("点选内容与当前提议不一致，请刷新后重试。");
        s.CurrentChoice=new(question!.Id,question.Text,option.Id,option.Text);
        return option;
    }

    public static bool Expedite(string message)=>new[]{"快点","别问了","不要再问","直接安排","直接给我草案","先看草案"}.Any(message.Contains);

    // Server-owned shortcuts never require a model call or a name to proceed.
    public static JsonObject? DirectResponse(PetSession s,string text,PetQuickReply? choice)
    {
        if(s.Dialogue is not {} d)return null;
        if(choice?.Intent=="draft" || Expedite(text))return new(){["reply"]="先用已有的线索构想一版，未知的资料可以之后一起补。",["readyForDraft"]=true};
        if(choice?.Intent=="keep")return new(){["reply"]="这个方向留在这次草案里了。想调整哪一段，随时告诉我。"};
        var match=Regex.Match(text,@"^(?:叫我|我叫|称呼我)\s*([^,，。!！？\r\n]{1,24})[。！!]?$");
        // A single name in direct response to the optional name question is explicit evidence.
        var shortName=d.Phase=="introducing" && choice is null && Regex.IsMatch(text,@"^[A-Za-z][A-Za-z'’-]{0,23}$") &&
            !new[]{"skip","hello","hi","yes","no","unknown"}.Contains(text,StringComparer.OrdinalIgnoreCase);
        if(choice?.Intent=="use_alias")d.Nickname="旅行搭子";
        else if(match.Success)d.Nickname=match.Groups[1].Value.Trim();
        else if(shortName)d.Nickname=text;
        if(d.Phase!="introducing")return null;
        if(choice?.Intent is not ("skip_name" or "use_alias") && !match.Success && !shortName)return null;
        return new(){["reply"]=(d.Nickname.Length>0 ? $"{d.Nickname}，咱们先找一点这趟旅行的灵感。":"咱们先找一点这趟旅行的灵感。")+"留一段自由探索，还是围绕一个最想体验的事展开？",
            ["quickReplies"]=new JsonArray(new JsonObject{["text"]="留一段自由探索"},new JsonObject{["text"]="美食或城市故事"},new JsonObject{["text"]="先看看你的想法",["intent"]="draft"})};
    }

    public static PetMessage CompleteTurn(PetSession s,JsonObject result,string reply,string mood)
    {
        if(s.Dialogue is not {} d)return new(Guid.NewGuid().ToString("N"),"assistant",reply,mood);
        var initial=d.Inspiration is null;
        if(initial)d.ResponseCount=Math.Min(MaximumResponses,d.ResponseCount+1);
        var ideas=(result.Array("ideas") ?? new JsonArray()).OfType<JsonValue>()
            .Select(x=>x.TryGetValue<string>(out var t) ? t.Trim():"").Where(x=>x.Length is >0 and <=240).Distinct().Take(3).ToArray();
        if(ideas.Length>0)d.Ideas=ideas;
        var finish=d.Inspiration is not null || d.ResponseCount>=MaximumResponses || result.Bool("readyForDraft");
        PetQuickReply[] options;
        if(finish) {
            Finish(s,d.Ideas);
            // The fourth answer ends the initial interview even when the model asks another question.
            if(initial)reply="先把这趟的方向摆出来了。下面是可以一起调整的灵感，具体场所、时间和费用等资料齐了再核实。";
            options=DraftReplies();
        } else {
            d.Phase="exploring";
            var proposed=(result.Array("quickReplies") ?? new JsonArray()).OfType<JsonObject>().Take(4)
                .Where(x=>x.Str("text").Length is >0 and <=45)
                .Select(x=>new PetQuickReply(Guid.NewGuid().ToString("N"),x.Str("text"),x.Str("intent") is "draft" or "skip" ? x.Str("intent"):"respond"))
                .DistinctBy(x=>x.Text).ToArray();
            if(proposed.Length is >=3 and <=4 && proposed.Any(x=>x.Intent is "skip" or "draft"))options=proposed;
            else {
                // Safe generic reactions bind to the actual proposal; no unrelated field questionnaire.
                options=[Reply("就按这个想法试试"),Reply("换个想法吧","skip"),Reply("先看已有的草案","draft")];
                if(string.IsNullOrWhiteSpace(reply))reply="咱们先留一段可以随心逛逛的时间，再围绕你最想体验的事串起来？";
            }
        }
        return new(Guid.NewGuid().ToString("N"),"assistant",reply,mood,options);
    }

    private static void Finish(PetSession s,string[] ideas)
    {
        var d=s.Dialogue!;d.Phase="inspiration";
        if(ideas.Length==0)ideas=["先留一段自由探索，再挑一件最期待的体验串成路线；具体方向还可以调整。"];
        d.Ideas=ideas;
        d.Inspiration=new(PetQuestions.Known(s.Fields.GetValueOrDefault("destination")) ? s.Fields["destination"]+" · 旅行灵感":"目的地待定 · 旅行灵感",ideas);
    }
    private static PetQuickReply Reply(string text,string intent="respond")=>new(Guid.NewGuid().ToString("N"),text,intent);
    private static PetQuickReply[] NameReplies()=>[Reply("先跳过称呼","skip_name"),Reply("叫我旅行搭子","use_alias"),Reply("我来写个称呼","compose_name")];
    private static PetQuickReply[] ExplorationReplies()=>[Reply("留一段自由探索"),Reply("美食或城市故事"),Reply("先看看你的想法","draft")];
    private static PetQuickReply[] DraftReplies()=>[Reply("先保留这个方向","keep"),Reply("换个思路吧"),Reply("补充出行资料","details")];
}
