using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

public sealed record PetMessage(string Id, string Role, string Text, string Mood="greeting");
public static class PetEmotion
{
    public static string Normalize(string value)=>value is "curious" or "excited" or "relaxed" or "remembering" or "confused" ? value:"curious";
}
public sealed class PetSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<PetMessage> Messages { get; set; } = [];
    public Dictionary<string,string> Fields { get; set; } = [];
    public string PendingMemory { get; set; } = "";
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
    public static readonly string[] Keys=["destination","date","days","companions","budget","interests","pace","notes"];
    public static readonly Dictionary<string,string> Labels=new()
    {
        ["destination"]="目的地",["date"]="出发日期",["days"]="天数",["companions"]="同行伙伴",
        ["budget"]="每人每日活动预算（AUD）",["interests"]="这次想玩什么",["pace"]="旅行节奏",["notes"]="补充要求"
    };
    public static string Next(PetSession s)=>Keys.FirstOrDefault(k=>!s.Fields.ContainsKey(k))??"";
    public static string Question(string key)=>key switch
    {
        "destination"=>"这次想去哪里呀？我先把目的地放进小背包。",
        "date"=>"准备哪天出发？没确定也可以先跳过。",
        "days"=>"想留几天给这次小冒险？",
        "companions"=>"这次和谁一起出发呀？",
        "budget"=>"每人每天的活动预算大概多少澳元？住宿和大交通另算哦。",
        "interests"=>"这次最想发现什么？也可以直接告诉我好几个喜好。",
        "pace"=>"这次想慢悠悠，还是多逛几个地方？",
        "notes"=>"还有饮食、步行或其他需要照顾的地方吗？没有就跳过吧。",
        _=>"小背包准备好啦！你可以继续聊，也可以检查信息，生成行程草案。"
    };
    public static string[] Choices(string key)=>key switch
    {
        "destination"=>["悉尼","墨尔本","还没想好"],"date"=>["今天","明天","还没确定"],
        "days"=>["1","2","3"],"companions"=>["一个人","两个人","和朋友","和家人"],
        "budget"=>["50","100","200"],"interests"=>["艺术、文化","美食、城市","自然、风景"],
        "pace"=>["慢悠悠（约6000步）","适中（约10000步）","多逛逛（约15000步）"],
        "notes"=>["喜欢安静","需要多休息","没有其他要求"],_=>[]
    };
    public static object View(PetSession s)=>new {s.Id,s.Messages,s.Fields,s.PendingMemory,s.UpdatedAt,nextField=Next(s),question=Question(Next(s)),options=Choices(Next(s)),completed=s.Fields.Count, total=Keys.Length};
    public static void Apply(PetSession s,JsonObject fields)
    {
        foreach(var key in Keys)
            if(fields[key] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                if(text.Length>600) text=text[..600];
                if(text.Trim().Length==0) s.Fields.Remove(key); else s.Fields[key]=text.Trim();
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
