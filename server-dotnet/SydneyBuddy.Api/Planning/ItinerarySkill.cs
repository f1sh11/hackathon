using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SydneyBuddy.Api;

/// Append-only skill bundles are deployed by an operator, never through the shared App token.
public static class ItinerarySkill
{
    public const string BaseVersion="2026-10-27.5";
    static readonly AsyncLocal<Snapshot?> Current=new();
    public sealed record Snapshot(string Version,string Source,string English,string Chinese)
    {
        public string Read(string language)=>language.StartsWith("en",StringComparison.OrdinalIgnoreCase)?English:Chinese;
        public string Hash(string language)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Read(language))));
    }
    static readonly Lazy<Snapshot> Embedded=new(()=>new(BaseVersion,"embedded",EmbeddedRead("en"),EmbeddedRead("zh-Hant")));
    public static string Version=>(Current.Value??Embedded.Value).Version;
    public static string Source=>(Current.Value??Embedded.Value).Source;
    public static string Read(string language)=>(Current.Value??Embedded.Value).Read(language);
    public static string Hash(string language)=>(Current.Value??Embedded.Value).Hash(language);
    static string EmbeddedRead(string language)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SydneyBuddy.Api.Planning.Skills.itinerary."+language+".md")
            ??throw new ServiceException("行程規劃 Skill 尚未載入。");
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public static Snapshot Load(string? path)
    {
        if(string.IsNullOrEmpty(path)||!File.Exists(path))return Embedded.Value;
        try{
            using var stream=File.OpenRead(path);
            if(stream.Length>150_000)throw new InvalidDataException();
            var bundle=JsonNode.Parse(stream)!.AsObject();
            var version=bundle.Str("version");var en=bundle.Str("enAppendix");var zh=bundle.Str("zhHantAppendix");
            if(bundle.Str("baseVersion")!=BaseVersion || version==BaseVersion || !Regex.IsMatch(version,@"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}\z") ||
               string.IsNullOrWhiteSpace(en)||string.IsNullOrWhiteSpace(zh)||Encoding.UTF8.GetByteCount(en)>64_000||Encoding.UTF8.GetByteCount(zh)>64_000)throw new InvalidDataException();
            return new(version,"operator_bundle",Embedded.Value.English+"\n\n"+en,Embedded.Value.Chinese+"\n\n"+zh);
        }catch(Exception e) when(e is not OperationCanceledException){throw new ServiceException("Skill 更新檔無效；請由管理者回復先前版本。");}
    }
    public static IDisposable BeginRequest(string? configuredPath=null)
    {
        var path=configuredPath??Environment.GetEnvironmentVariable("ITINERARY_SKILL_BUNDLE");
        var data=Environment.GetEnvironmentVariable("SYDNEYBUDDY_DATA");
        if(string.IsNullOrEmpty(path)&&!string.IsNullOrEmpty(data))path=Path.Combine(data,"planning-skills","active.json");
        return Use(Load(path));
    }
    public static IDisposable Use(Snapshot snapshot){var previous=Current.Value;Current.Value=snapshot;return new Scope(()=>Current.Value=previous);}
    sealed class Scope(Action restore):IDisposable {public void Dispose()=>restore();}
}
