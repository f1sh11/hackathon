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
    public string ResponseLanguage { get; set; } = "en";
    public List<PetMessage> Messages { get; set; } = [];
    public Dictionary<string,string> Fields { get; set; } = [];
    public JsonObject PersonalProfile { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public JsonArray ActiveMemories {get;set;}=[];
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
        if (!Guid.TryParseExact(id,"N",out _)) throw new ServiceException("Invalid conversation ID.");
        return Path.Combine(DirectoryPath,id+".json");
    }
    public PetSession Load(string id)
    {
        var path=FilePath(id);
        if (!File.Exists(path)) throw new ServiceException("This conversation has been cleared. Start a new one.");
        var session=JsonSerializer.Deserialize<PetSession>(File.ReadAllText(path),JsonExtensions.Options)!;
        session.ResponseLanguage="en";
        PetEnglishCopy.Refresh(session);
        return session;
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

// Interface language is session metadata, never a travel preference or memory.
public static class PetResponseLanguage
{
    // English is the product language, including drafts saved with a previous locale.
    // Keep accepting the language field for older clients without making it a preference.
    public static void Apply(PetSession session,JsonObject input)=>session.ResponseLanguage="en";
    public static string Prompt(PetSession session)=>"\nWrite all user-visible replies, questions, option labels and trip inspiration in English, even if the user writes in another language or the saved conversation contains another language. Keep JSON keys, enum values, place names, evidence quotes and the user's facts unchanged. The interface language does not change travel preferences or memory consent.";

}

public static class PetQuestions
{
    public static readonly string[] Keys=["destination","date","days","companions","budget","interests","pace","notes","budgetCurrency","budgetBasis","travelers","origin","mustDo","avoid","energy","rhythm","queueTolerance","budgetCoverage","flights","bookings","tickets","companionNeeds","specialLimits","dayStyle","transportPreference"];
    public static readonly Dictionary<string,string> Labels=new()
    {
        ["destination"]="Destination",["date"]="Departure date",["days"]="Days",["companions"]="Travel companions",
        ["budget"]="Budget amount",["interests"]="Trip interests",["pace"]="Travel pace",["notes"]="Additional needs"
    };
    public static readonly string[] Required=["destination","date","days","budget","budgetCurrency","budgetBasis","travelers"];
    public static readonly string[] Optional=["origin","companions","mustDo","avoid"];
    public static bool Known(string? value)=>!new[]{"","未确定","暂不确定","未知","稍后补充","还没确定","还没想好","未提供","unknown","不确定","不知道","Not sure yet","Not decided yet","Not determined","Undecided","Unknown","Add later","Not provided","Unsure","Don’t know","Don't know"}.Contains((value??"").Trim(),StringComparer.OrdinalIgnoreCase);
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
        "destination"=>"Let’s give our map a starting point. Which city are you thinking of? 🐨",
        "date"=>"Let’s pencil in a departure date. You can always change it later.",
        "days"=>"How many days should we plan for? Choose 1 to 15 days; we can adjust it later.",
        "budget"=>"What’s a comfortable activity budget? A rough amount is fine for now.",
        "budgetCurrency"=>"Which currency is that: AUD, CNY or USD?",
        "budgetBasis"=>"Is that per person or for everyone, and per day or for the whole trip?",
        "travelers"=>"How many people are coming, including you?",
        _=>"We can leave that open for now."
    };
    public static string Question(string key)=>key switch
    {
        "destination"=>"Where would you like to go? I can plan Sydney routes now, or save ideas for another city.",
        "date"=>"When would you like to leave? You can change the date later.",
        "days"=>"How many days do you have for this little adventure?",
        "companions"=>"Who’s coming along?",
        "budget"=>"What’s your activity budget? Include the currency, whether it’s per person or for everyone, and per day or for the whole trip.",
        "budgetCurrency"=>"Which currency is your activity budget in? Planning currently uses AUD; for another currency, we’ll need you to confirm an AUD amount.",
        "budgetBasis"=>"Is this amount per person or for everyone, and per day or for the whole trip?",
        "travelers"=>"How many people are travelling, including you?",
        "origin"=>"Do you have a starting point or a place to stay? You can add it later.",
        "mustDo"=>"Is there a place or experience you really want to include? It’s fine to skip this.",
        "avoid"=>"Is there anything to avoid, or any travel needs I should consider?",
        "interests"=>"What would you love to discover? You can mention a few interests.",
        "pace"=>"Would you rather take it slow or fit in a few more places?",
        "notes"=>"Any food, walking or other needs to consider? You can skip this.",
        _=>"We’re ready to plan! Tap Generate itinerary below, or keep chatting."
    };
    public static string[] Choices(string key)=>key switch
    {
        "destination"=>["Sydney","Not sure yet"],"date"=>["Today","Tomorrow","Not decided yet"],
        "days"=>["1","3","7","15"],"companions"=>["Solo","Two people","With friends","With family"],
        "budget"=>["50","100","200"],"interests"=>["Art and culture","Food and city life","Nature and scenery"],
        "pace"=>["Easy strolls (about 6,000 steps)","Moderate (about 10,000 steps)","Explore more (about 15,000 steps)"],
        "budgetCurrency"=>["AUD"],"budgetBasis"=>["perPersonPerDay","groupPerDay","perPersonTrip","groupTrip"],"travelers"=>["1","2","3","4"],"origin"=>["Circular Quay","Central Station","Add later"],"mustDo"=>["Undecided"],"avoid"=>["No special requirements","More rest breaks","Avoid long walks"],
        "notes"=>["Prefer quiet places","More rest breaks","No other requirements"],_=>[]
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
                if(key=="energy") {text=text switch {"低" or "low" or "Low energy"=>"低精力","中等" or "中" or "moderate" or "Moderate energy"=>"中等精力","高" or "high" or "High energy"=>"高精力",_=>text};if(text.Length>0 && text is not ("低精力" or "中等精力" or "高精力"))continue;}
                if(key=="rhythm") {text=text switch {"轻松" or "慢悠悠" or "relaxed" or "Relaxed" or "Easy-going"=>"轻松随意","中等" or "moderate" or "Moderate"=>"适中","紧凑" or "busy" or "Busy"=>"紧凑充实",_=>text};if(text.Length>0 && text is not ("轻松随意" or "适中" or "紧凑充实"))continue;}
                if(text.Length==0)s.Fields.Remove(key);else s.Fields[key]=text;
                if(key=="companions" && !s.Fields.ContainsKey("travelers") && text is "一个人" or "两个人" or "Solo" or "Two people")s.Fields["travelers"]=text is "一个人" or "Solo" ? "1":"2";
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
    public static bool MentionsBudget(string message) => System.Text.RegularExpressions.Regex.IsMatch(message,
        @"预算|預算|澳元|澳币|澳幣|人民币|人民幣|美元|美金|AUD|CNY|USD|RMB|budget|dollars?|每人|全体|全體|全程|总共|一共|^\s*[0-9]+(?:\.[0-9]+)?\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static void FilterUnsupportedHabits(string message,JsonObject fields)
    {
        if(!MentionsBudget(message))foreach(var key in new[]{"budget","budgetCurrency","budgetBasis","budgetCoverage"})fields.Remove(key);
        var evidence=new Dictionary<string,string>{
            ["energy"]=@"精力|体力|活力|電量|电量|\b(?:energy|fitness|stamina|tired|rest)\b",
            ["rhythm"]=@"节奏|節奏|轻松|輕鬆|紧凑|緊湊|适中|慢慢|慢逛|悠闲|充实|\b(?:pace|relaxed|relaxing|slow|moderate|busy|packed|easy|leisurely)\b",
            ["pace"]=@"(?:[0-9一二两三四五六七八九十百千万][0-9一二两三四五六七八九十百千万,，]*\s*步|步[数數]\s*[:：]?\s*[0-9]|慢悠悠|少走|多走|[0-9][0-9,]*\s*steps?|less walking|more walking)",
            ["queueTolerance"]=@"排队|排隊|等位|等候|\b(?:queues?|queuing|queueing|wait|waiting)\b",
            ["interests"]=@"兴趣|興趣|喜欢|喜歡|爱好|愛好|自然|风景|風景|美食|文化|艺术|藝術|购物|購物|城市|\b(?:interests?|like|love|enjoy|prefer|nature|scenery|scenic|food|culture|art|shopping|city)\b"
        };
        foreach(var item in evidence)if(fields.ContainsKey(item.Key) && !System.Text.RegularExpressions.Regex.IsMatch(message,item.Value,System.Text.RegularExpressions.RegexOptions.IgnoreCase))fields.Remove(item.Key);
    }

    public static void Apply(string message,JsonObject fields)
    {
        message=System.Text.RegularExpressions.Regex.Replace(message,@"(?:没有|无)(?:什么|特别|任何)?(?:的)?必去(?:地点|的地方|景点)?(?:和|或|、)(?:需要)?避开(?:的安排|的地方|安排)?","没有必去地点，没有需要避开的安排");
        bool Ambiguous(System.Text.RegularExpressions.Match match) {
            char[] separators=['，',',','。','；',';','！','!','？','?','\n'];
            var left=match.Index==0 ? -1:message.LastIndexOfAny(separators,match.Index-1);
            var right=message.IndexOfAny(separators,match.Index);
            var clause=message[(left+1)..(right<0 ? message.Length:right)];
            return System.Text.RegularExpressions.Regex.IsMatch(clause,@"不是|不要|不想|还没确定|或者|或|到|至");
        }
        // Explicit hard constraints must survive a model omitting optional fields once essentials are complete.
        bool Extract(string key,string pattern) {
            var values=System.Text.RegularExpressions.Regex.Matches(message,pattern)
                .Where(m=>!System.Text.RegularExpressions.Regex.IsMatch(message[..m.Index],@"(?:(?:没有|无)(?:什么|特别|任何)?|不是|无需|不用|不需要|不必|不指定|不设定|不要求|不强求|不)\s*$"))
                .Select(m=>System.Text.RegularExpressions.Regex.Split(m.Groups[1].Value,"但是|不过|但")[0].Trim())
                .Where(v=>v.Length is >0 and <=120)
                .Where(v=>key!="mustDo" || !System.Text.RegularExpressions.Regex.IsMatch(v,@"哪|什么|不确定|未确定|待定|或者|或|随便|都行|^(?:悉尼|Sydney)(?:旅行|旅游|玩\s*[0-9一二两三四五六七八九十]+\s*天)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Distinct().ToArray();
            if(values.Length>0)fields[key]=string.Join("、",values);
            return values.Length>0;
        }
        Extract("mustDo",@"(?:必须去|一定要去|特别想去|想去|想逛|想参观|必去(?:地点)?(?:是|：|:)?)\s*([^，,。；;！？\n]+)");
        var hasExplicitAvoid=Extract("avoid",@"(?:必须避开|需要避开|不去|不逛|不参观|不想(?:去|逛|参观)|不要(?:去|逛|参观)|避免(?:去|逛|参观)|避开)\s*([^，,。；;！？\n]+)");
        // Explicit trip interests must not disappear when the model extracts only essential fields.
        var interestClauses=System.Text.RegularExpressions.Regex.Split(message,@"[，,。；;！？\n]")
            .Where(c=>System.Text.RegularExpressions.Regex.IsMatch(c,"想|喜欢|喜歡|兴趣|興趣|体验|體驗|偏好|主要") &&
                !System.Text.RegularExpressions.Regex.IsMatch(c,"不想|不喜欢|不喜歡|不要|不感兴趣|不感興趣|避开|避免"));
        var explicitInterests=new[]{"美食","自然","风景","文化","艺术","购物","城市"}
            .Where(i=>interestClauses.Any(c=>c.Contains(i) || (i=="美食" && System.Text.RegularExpressions.Regex.IsMatch(c,"想吃|逛吃|food",System.Text.RegularExpressions.RegexOptions.IgnoreCase))))
            .ToArray();
        if(explicitInterests.Length>0)fields["interests"]=string.Join("、",explicitInterests);
        // Keep unambiguous travel facts even when the conversational model omits fields.
        if(System.Text.RegularExpressions.Regex.IsMatch(message,@"(?:去|目的地(?:是|：|:)?)\s*(?:悉尼|Sydney)(?=[，,。\s]|玩|旅行|旅游|旅遊|待|呆|出发|出發|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
            !System.Text.RegularExpressions.Regex.IsMatch(message,@"(?:不去|不要去|不想去|不是)\s*(?:悉尼|Sydney)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))fields["destination"]="悉尼";
        var durations=System.Text.RegularExpressions.Regex.Matches(message,@"(?:玩|旅行|待|呆|停留|行程|总共|共)\s*([1-9]\d{0,2})\s*天")
            .Where(m=>!Ambiguous(m)).Select(m=>m.Groups[1].Value).Distinct().ToArray();
        if(durations.Length==1)fields["days"]=durations[0];
        var dates=System.Text.RegularExpressions.Regex.Matches(message,@"(?<!\d)(20\d{2}-\d{2}-\d{2})(?!\d)").Where(m=>!Ambiguous(m)).Select(m=>m.Groups[1].Value).Distinct().ToArray();
        if(dates.Length==1 && DateOnly.TryParseExact(dates[0],"yyyy-MM-dd",out _))fields["date"]=dates[0];
        else if(dates.Length==0) {
            var relative=System.Text.RegularExpressions.Regex.Matches(message,"今天|明天|后天").Where(m=>!Ambiguous(m)).Select(m=>m.Value).Distinct().ToArray();
            if(relative.Length==1) {
                var today=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Sydney").Date;
                fields["date"]=today.AddDays(relative[0]=="明天" ? 1:relative[0]=="后天" ? 2:0).ToString("yyyy-MM-dd");
            }
        }
        if(System.Text.RegularExpressions.Regex.IsMatch(message,"只步行|全程步行|不要坐车|不坐公交|walk only",System.Text.RegularExpressions.RegexOptions.IgnoreCase))fields["transportPreference"]="walk";
        else if(System.Text.RegularExpressions.Regex.IsMatch(message,"尽量少走|少走路|多坐车|less walking",System.Text.RegularExpressions.RegexOptions.IgnoreCase))fields["transportPreference"]="lessWalking";
        else if(System.Text.RegularExpressions.Regex.IsMatch(message,"公交|公共交通|火车|渡轮|地铁|轻轨|transit",System.Text.RegularExpressions.RegexOptions.IgnoreCase))fields["transportPreference"]="balanced";
        // Exact numeric limits outrank a model's qualitative "轻松" summary.
        var walkingText=System.Text.RegularExpressions.Regex.Replace(message,@"(?<=\d)[,，](?=\d{3}(?!\d))","");
        var walking=System.Text.RegularExpressions.Regex.Matches(walkingText,@"(?<![\d\-–~到至])([1-9]\d{2,4})\s*步")
            .Concat(System.Text.RegularExpressions.Regex.Matches(walkingText,@"(?:步数|步數)\s*[:：]?\s*([1-9]\d{2,4})(?!\d|\s*[-–~到至])"))
            .Select(m=>m.Groups[1].Value).Where(v=>int.TryParse(v,out var n) && n is >=500 and <=60000).Distinct().ToArray();
        if(walking.Length==1)fields["pace"]="约"+walking[0]+"步";
        var rhythms=new[]{("轻松随意",@"轻松随意|想轻松一点|想轻松些|慢慢逛|悠闲一点"),("紧凑充实",@"紧凑充实|想紧凑一点|充实一点"),("适中",@"节奏适中|适中节奏")}
            .Where(r=>System.Text.RegularExpressions.Regex.Matches(message,r.Item2).Any(m=>!Ambiguous(m))).Select(r=>r.Item1).Distinct().ToArray();
        if(rhythms.Length==1)fields["rhythm"]=rhythms[0];


        if(System.Text.RegularExpressions.Regex.IsMatch(message,@"(?:没有|无|不指定|不设定?|不要求|不强求)(?:什么|特别|任何|固定|特定)?(?:的)?必去(?:地点|的地方|景点)?") && !message.Contains("但是") && !message.Contains("但想"))fields["mustDo"]="无";
        if(!hasExplicitAvoid && System.Text.RegularExpressions.Regex.IsMatch(message,@"(?:没有|无)(?:什么|特别|任何)?(?:需要)?避开(?:的安排|的地方|安排)?"))fields["avoid"]="无";
        var amount=System.Text.RegularExpressions.Regex.Matches(message,@"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*(澳元|澳币|AUD|人民币|CNY|美元|USD)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var amountExplicit=amount.Count==1 && !System.Text.RegularExpressions.Regex.IsMatch(message[..amount[0].Index],@"(?:不是|并非|不要|别用|还是|或者|或|到|至|[-~–])\s*$");
        if(amountExplicit){fields["budget"]=amount[0].Groups[1].Value;fields["budgetCurrency"]=amount[0].Groups[2].Value;}
        var mentions=System.Text.RegularExpressions.Regex.Matches(message,@"([0-9]{1,2}|一|二|两|三|四|五|六|七|八|九|十)\s*(?:个)?人");
        var counts=mentions.Select(m=>m.Groups[1].Value switch {"一"=>"1","二" or "两"=>"2","三"=>"3","四"=>"4","五"=>"5","六"=>"6","七"=>"7","八"=>"8","九"=>"9","十"=>"10",_=>m.Groups[1].Value}).Distinct().ToArray();
        if(counts.Length==1 && !message.Contains("不是")) {
            fields["travelers"]=counts[0];
            if(counts[0] is "1" or "2")fields["companions"]=counts[0]=="1" ? "一个人":"两个人";
        }
        if(amountExplicit && System.Text.RegularExpressions.Regex.IsMatch(message,@"每人(?:每天|每日)|(?:每天|每日)每人"))fields["budgetBasis"]="perPersonPerDay";
        else if(amountExplicit && System.Text.RegularExpressions.Regex.IsMatch(message,@"全体(?:每天|每日)|(?:每天|每日)(?:全体|总共|一共)"))fields["budgetBasis"]="groupPerDay";
        if(amountExplicit && message.Contains("活动预算"))fields["budgetCoverage"]="activities";
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
        var basisLabel=basis switch {"perPersonPerDay"=>"per person per day","groupPerDay"=>"for the group per day","perPersonTrip"=>"per person for the whole trip","groupTrip"=>"for the group for the whole trip",_=>"scope to confirm"};
        var reply=$"Noted: {amount.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)} {currency}, {basisLabel}, for this trip.";
        if(currency=="AUD" && int.TryParse(f.GetValueOrDefault("days"),out var days) && days is >=1 and <=15 && int.TryParse(f.GetValueOrDefault("travelers"),out var people) && people is >=1 and <=30 && basisLabel!="scope to confirm") {
            var perDay=amount/(basis.StartsWith("group") ? people:1)/(basis.EndsWith("Trip") ? days:1);
            reply+=$" That’s about {perDay.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)} AUD per person per day for activities.";
        }
        return reply;
    }
}
