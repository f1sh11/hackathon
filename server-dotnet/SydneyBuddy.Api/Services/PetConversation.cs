using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

public sealed record PetMessage(string Id, string Role, string Text, string Mood="greeting",
    PetQuickReply[]? QuickReplies=null, PetChoiceEvidence? Choice=null);
public static class PetEmotion
{
    public static string Normalize(string value)=>value is "curious" or "excited" or "relaxed" or "remembering" or "confused" or "pouting" ? value:"curious";
}
public sealed class PetSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<PetMessage> Messages { get; set; } = [];
    public Dictionary<string,string> Fields { get; set; } = [];
    public JsonObject PersonalProfile { get; set; } = new();
    public string PendingMemory { get; set; } = "";
    public List<TravelPreference> Preferences { get; set; } = [];
    public List<TravelPreference> ConfirmedDna { get; set; } = [];
    public string MemoryWarning { get; set; } = "";
    public bool DnaCompleted { get; set; }
    public ProfilingDialogue? Dialogue { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public PetChoiceEvidence? CurrentChoice { get; set; }
    public List<string> CompletedRequests { get; set; } = [];
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

// Local single-user service. A session is one journey, never reused for a new trip.
// Atomic private files keep drafts independent of accepted trip revisions.
public sealed class PetSessionStore(ProjectPaths paths)
{
    public SemaphoreSlim Gate { get; } = new(1,1);
    private string DirectoryPath => Path.Combine(paths.DataDirectory,".pet-sessions");
    private string FilePath(string id)
    {
        if (!Guid.TryParseExact(id,"N",out _)) throw new ServiceException("对话编号无效。");
        return Path.Combine(DirectoryPath,id+".json");
    }
    public PetSession Load(string id)
    {
        var path=FilePath(id);
        if (!File.Exists(path)) throw new ServiceException("这段对话已清空，请开启新对话。");
        return JsonSerializer.Deserialize<PetSession>(File.ReadAllText(path),JsonExtensions.Options)!;
    }
    public void Save(PetSession session)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(DirectoryPath,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        session.UpdatedAt=DateTimeOffset.UtcNow.ToString("O");
        var path=FilePath(session.Id); var temporary=path+".tmp";
        var options=new FileStreamOptions { Mode=FileMode.Create,Access=FileAccess.Write };
        if(!OperatingSystem.IsWindows()) options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        using(var stream=new FileStream(temporary,options))
            JsonSerializer.Serialize(stream,session,JsonExtensions.Options);
        File.Move(temporary,path,true);
    }
    public void Delete(string id) => File.Delete(FilePath(id));
}

public static class PetQuestions
{
    public static readonly string[] Keys=["destination","date","days","companions","budget","interests","pace","notes","budgetCurrency","budgetBasis","travelers","origin","mustDo","avoid","energy","rhythm","queueTolerance","budgetCoverage","flights","bookings","tickets","companionNeeds","specialLimits","dayStyle"];
    public static readonly Dictionary<string,string> Labels=new()
    {
        ["destination"]="目的地",["date"]="出发日期",["days"]="天数",["companions"]="同行伙伴",
        ["budget"]="预算金额",["interests"]="这次想玩什么",["pace"]="旅行节奏",["notes"]="补充要求"
    };
    public static readonly string[] Required=["destination","date","days","budget","budgetCurrency","budgetBasis","travelers"];
    public static readonly string[] Optional=["origin","companions","mustDo","avoid"];
    public static bool Known(string? value)=>!new[]{"","未确定","暂不确定","未知","稍后补充","还没确定","还没想好","未提供","unknown","不确定","不知道"}.Contains((value??"").Trim());
    public static bool SupportedDestination(string? value)=>new[]{"悉尼","雪梨","sydney"}.Contains((value??"").Trim().ToLowerInvariant());
    public static bool UnsupportedDestination(PetSession s)=>Known(s.Fields.GetValueOrDefault("destination")) && !SupportedDestination(s.Fields.GetValueOrDefault("destination"));
    public static bool Answered(PetSession s,string key) {
        var value=s.Fields.GetValueOrDefault(key);if(!Known(value))return false;
        return key switch {
            "destination"=>SupportedDestination(value),
            "date"=>DateOnly.TryParseExact(value,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _),
            "days"=>int.TryParse(value,out var days) && days is >=1 and <=15,
            "travelers"=>int.TryParse(value,out var people) && people is >=1 and <=30,
            "budget"=>ValidBudget(s,value),
            "budgetCurrency"=>value=="AUD",
            "budgetBasis"=>new[]{"perPersonPerDay","groupPerDay","perPersonTrip","groupTrip"}.Contains(value),
            _=>true
        };
    }
    private static bool ValidBudget(PetSession s,string? value) {
        if(s.Fields.GetValueOrDefault("budgetCoverage")=="totalTravel" || !double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var amount) || !double.IsFinite(amount) || amount<=0 || amount>1000000)return false;
        var basis=s.Fields.GetValueOrDefault("budgetBasis","");
        if(int.TryParse(s.Fields.GetValueOrDefault("days"),out var days) && days>0 && int.TryParse(s.Fields.GetValueOrDefault("travelers"),out var people) && people>0){
            var daily=amount/(basis.StartsWith("group") ? people:1)/(basis.EndsWith("Trip") ? days:1);return daily<=10000;
        }
        return true;
    }
    public static string Next(PetSession s)=>Required.FirstOrDefault(k=>!Answered(s,k)) ?? (s.Fields.GetValueOrDefault("budgetCoverage")=="totalTravel" ? "budget":"");
    public static string UncertainReply(string key)=>key switch {
        "destination"=>"哼唧，小爪爪抱住地图！还不知道去哪儿，我可没法带路呀～先选一个城市，好不好？🐨",
        "date"=>"哼，小日历都摊开啦！先挑个出发日期嘛，之后还可以改～",
        "days"=>"小背包鼓起脸啦！玩几天得先告诉我呀～选 1 到 15 天，我们再慢慢调。",
        "budget"=>"哼唧，小钱包还空着呢！先给我一个大概的活动预算嘛，之后可以改～",
        "budgetCurrency"=>"小爪爪停一下！是澳元、人民币还是美元呀？币种不清楚，我会算糊涂的～",
        "budgetBasis"=>"哼，这笔钱是每人还是大家一起、每天还是全程呀？说清楚，我才不会把小钱包算错嘛～",
        "travelers"=>"小爪爪开始数人头啦！一共几个人呀？记得把自己也算进去哦～",
        _=>"好呀，这个先留个小空位，不耽误我们出发～"
    };
    public static string Question(string key)=>key switch
    {
        "destination"=>"这次想去哪里呀？目前可以生成悉尼路线；其他城市可以先保存想法。",
        "date"=>"准备哪天出发？先选个日期，之后还可以改。",
        "days"=>"想留几天给这次小冒险？",
        "companions"=>"这次和谁一起出发呀？",
        "budget"=>"这次活动预算大概多少？请一起告诉我币种，以及是每人还是全体、每天还是全程。",
        "budgetCurrency"=>"这笔活动预算使用什么币种？目前支持澳元 AUD；其他币种需要先确认澳元金额。",
        "budgetBasis"=>"这笔金额是每人还是全体、每天还是全程呢？",
        "travelers"=>"这次总共几个人出发？包含你自己哦。",
        "origin"=>"有出发地点或住宿位置了吗？还不知道也可以稍后补充。",
        "mustDo"=>"这次特别想去哪里，或者想做什么？没有也可以先跳过。",
        "avoid"=>"有没有必须避开的安排，或需要特别照顾的出行需求？",
        "interests"=>"这次最想发现什么？也可以直接告诉我好几个喜好。",
        "pace"=>"这次想慢悠悠，还是多逛几个地方？",
        "notes"=>"还有饮食、步行或其他需要照顾的地方吗？没有就跳过吧。",
        _=>"小背包准备好啦！可以直接点下面的「生成行程」，也可以继续聊。"
    };
    public static string[] Choices(string key)=>key switch
    {
        "destination"=>["悉尼","还没想好"],"date"=>["今天","明天","还没确定"],
        "days"=>["1","3","7","15"],"companions"=>["一个人","两个人","和朋友","和家人"],
        "budget"=>["50","100","200"],"interests"=>["艺术、文化","美食、城市","自然、风景"],
        "pace"=>["慢悠悠（约6000步）","适中（约10000步）","多逛逛（约15000步）"],
        "budgetCurrency"=>["AUD"],"budgetBasis"=>["perPersonPerDay","groupPerDay","perPersonTrip","groupTrip"],"travelers"=>["1","2","3","4"],"origin"=>["环形码头","中央车站","稍后补充"],"mustDo"=>["暂不确定"],"avoid"=>["无特别要求","需要多休息","避免长时间步行"],
        "notes"=>["喜欢安静","需要多休息","没有其他要求"],_=>[]
    };
    public static object View(PetSession s)=>new {s.Id,s.Messages,s.Fields,s.PendingMemory,s.UpdatedAt,s.MemoryWarning,s.DnaCompleted,s.Dialogue,travelDna=TravelProfiling.Dna(s),tripContext=s.Fields,planningFields=TravelProfiling.PlanningFields(s),pendingPreferences=s.Preferences.Where(x=>x.Status=="awaiting_confirmation"),tripPreferences=s.Preferences.Where(x=>x.Status=="trip_only"),missingImportantFields=TravelProfiling.Missing(s),nextField=Next(s),question=Question(Next(s)),options=Choices(Next(s)),completed=Required.Count(k=>Answered(s,k)), total=Required.Length};
    public static void Apply(PetSession s,JsonObject fields)
    {
        foreach(var key in Keys)
            if(fields[key] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                if(text.Length>600) text=text[..600];
                text=text.Trim();
                if(key=="budgetCurrency")text=text.ToUpperInvariant() switch {"澳元" or "澳币" or "澳幣"=>"AUD","人民币" or "人民幣" or "RMB"=>"CNY","美元" or "美金"=>"USD",_=>text.ToUpperInvariant()};
                if(key=="budgetCoverage")text=text switch {"活动预算" or "活動預算" or "活动"=>"activities","全旅行" or "全部旅行费用" or "含住宿交通"=>"totalTravel",_=>text};
                if(key=="energy") {text=text switch {"低"=>"低精力","中等" or "中"=>"中等精力","高"=>"高精力",_=>text};if(text.Length>0 && text is not ("低精力" or "中等精力" or "高精力"))continue;}
                if(key=="rhythm") {text=text switch {"轻松" or "慢悠悠"=>"轻松随意","中等"=>"适中","紧凑"=>"紧凑充实",_=>text};if(text.Length>0 && text is not ("轻松随意" or "适中" or "紧凑充实"))continue;}
                if(text.Length==0)s.Fields.Remove(key);else s.Fields[key]=text;
                if(key=="companions" && !s.Fields.ContainsKey("travelers") && text is "一个人" or "两个人")s.Fields["travelers"]=text=="一个人"?"1":"2";
            }
    }
    public static string PartialReply(string json)
    {
        var marker=json.IndexOf("\"reply\"",StringComparison.Ordinal);
        if(marker<0) return "";
        var start=json.IndexOf(':',marker+7); if(start<0)return "";
        start++;while(start<json.Length&&char.IsWhiteSpace(json[start]))start++;
        if(start>=json.Length||json[start]!='"')return "";
        var end=start+1;
        while(end<json.Length)
        {
            if(json[end]=='"')break;
            if(json[end]=='\\') {if(end+1>=json.Length)break;if(json[end+1]=='u'){if(end+5>=json.Length)break;end+=6;}else end+=2;}
            else end++;
        }
        try{return JsonSerializer.Deserialize<string>(json[start..end]+"\"")??"";}catch(JsonException){return "";}
    }
}

/// Only unambiguous, explicit phrases are normalized here; unknowns stay unset.
public static class PetExplicitFacts
{
    public static void Apply(string message,JsonObject fields)
    {
        // Exact numeric limits outrank a model's qualitative "轻松" summary.
        var walkingText=System.Text.RegularExpressions.Regex.Replace(message,@"(?<=\d)[,，](?=\d{3}\s*步)","");
        var walking=System.Text.RegularExpressions.Regex.Matches(walkingText,@"(?<![\d\-–~到至])([1-9]\d{2,4})\s*步")
            .Select(m=>m.Groups[1].Value).Distinct().ToArray();
        if(walking.Length==1)fields["pace"]="约"+walking[0]+"步";

        if(System.Text.RegularExpressions.Regex.IsMatch(message,@"(?:没有|无)(?:什么|特别|任何)?必去(?:地点|的地方|景点)?") && !message.Contains("但是") && !message.Contains("但想"))fields["mustDo"]="无";
        if(System.Text.RegularExpressions.Regex.IsMatch(message,@"(?:没有|无)(?:什么|特别|任何)?(?:需要)?避开(?:的安排|的地方|安排)?"))fields["avoid"]="无";
        var amount=System.Text.RegularExpressions.Regex.Matches(message,@"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*(澳元|澳币|AUD|人民币|CNY|美元|USD)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var amountExplicit=amount.Count==1 && !System.Text.RegularExpressions.Regex.IsMatch(message[..amount[0].Index],@"(?:不是|并非|不要|别用|还是|或者|或|到|至|[-~–])\s*$");
        if(amountExplicit){fields["budget"]=amount[0].Groups[1].Value;fields["budgetCurrency"]=amount[0].Groups[2].Value;}
        var mentions=System.Text.RegularExpressions.Regex.Matches(message,@"([0-9]{1,2}|一|二|两|三|四|五|六|七|八|九|十)\s*(?:个)?人");
        var counts=mentions.Select(m=>m.Groups[1].Value switch {"一"=>"1","二" or "两"=>"2","三"=>"3","四"=>"4","五"=>"5","六"=>"6","七"=>"7","八"=>"8","九"=>"9","十"=>"10",_=>m.Groups[1].Value}).Distinct().ToArray();
        if(counts.Length==1 && !message.Contains("不是")) {
            fields["travelers"]=counts[0];
            if(counts[0] is "1" or "2")fields["companions"]=counts[0]=="1" ? "一个人":"两个人";
        }
        if(amountExplicit && message.Contains("全程")) {
            if(message.Contains("每人"))fields["budgetBasis"]="perPersonTrip";
            else if(message.Contains("一共") || message.Contains("全体") || message.Contains("总共") || System.Text.RegularExpressions.Regex.IsMatch(message,@"(?:[0-9]{1,2}|两|三|四|五|六|七|八|九|十)人全程"))fields["budgetBasis"]="groupTrip";
        }
        var points=new[]{"中央车站","环形码头","悉尼机场"}.Where(message.Contains).ToArray();
        if(points.Length==1 && !message.Contains("不从") && !message.Contains("不要从") && (message.Contains("出发") || message.Contains("住宿") || message.Contains("住在")))fields["origin"]=points[0];
        var liking=System.Text.RegularExpressions.Regex.Match(message,@"喜欢([^，。；!?！？\n]{1,50})");
        if(liking.Success && !new[]{"不喜欢","不怎么喜欢","不太喜欢"}.Any(message.Contains)) {
            var interests=new[]{"自然","风景","美食","文化","艺术","购物","城市"}.Where(liking.Groups[1].Value.Contains).ToArray();
            if(interests.Length>0)fields["interests"]=string.Join("、",interests);
        }
        if(message.Contains("这次") && message.Contains("轻松") && !new[]{"不想轻松","不要轻松","不轻松"}.Any(message.Contains))fields["rhythm"]="轻松随意";
    }
    public static string? BudgetReply(PetSession session)
    {
        var f=session.Fields;
        if(!double.TryParse(f.GetValueOrDefault("budget"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var amount) || !double.IsFinite(amount) || amount<=0)return null;
        var currency=f.GetValueOrDefault("budgetCurrency","");
        var basis=f.GetValueOrDefault("budgetBasis","");
        var basisLabel=basis switch {"perPersonPerDay"=>"每人每天","groupPerDay"=>"全体每天","perPersonTrip"=>"每人全程","groupTrip"=>"全体全程",_=>"范围待确认"};
        var reply=$"收到！预算 {amount.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)} {currency}（{basisLabel}），先收进这次的小背包。";
        if(currency=="AUD" && int.TryParse(f.GetValueOrDefault("days"),out var days) && days is >=1 and <=15 && int.TryParse(f.GetValueOrDefault("travelers"),out var people) && people is >=1 and <=30 && basisLabel!="范围待确认") {
            var perDay=amount/(basis.StartsWith("group") ? people:1)/(basis.EndsWith("Trip") ? days:1);
            reply+=$"换算为每人每日活动预算约 {perDay.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)} AUD。";
        }
        return reply;
    }
}
