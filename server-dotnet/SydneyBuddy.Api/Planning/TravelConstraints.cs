using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace SydneyBuddy.Api;

/// Resolves explicit requirements against verified catalog identities; unknown requests fail visibly.
public static class TravelConstraints
{
    public static IReadOnlyList<JsonObject> Apply(JsonObject profile,IReadOnlyList<JsonObject> places)
    {
        static bool Known(string text)=>!string.IsNullOrWhiteSpace(text) && text is not ("无" or "没有" or "无特别要求" or "未确定" or "暂不确定" or "稍后补充");
        static string Normalize(string text)=>Regex.Replace(text.ToLowerInvariant(),@"[\s，,、。；;！!]+","");
        bool Matches(JsonObject p,string term) {
            var names=new[]{p.Str("name"),p.Str("query"),p.Str("id"),p.Str("id") switch {"opera"=>"歌剧院", "garden"=>"植物园", "gallery"=>"美术馆", "mca"=>"当代艺术博物馆", "rocks"=>"岩石区", "qvb"=>"女王大厦", _=>p.Str("name")}};
            return names.Where(n=>!string.IsNullOrWhiteSpace(n)).Any(n=>Normalize(n)==Normalize(term) || Normalize(term).Contains(Normalize(n)));
        }
        var required=profile.Array("mustVisit")!.Select(x=>x!.GetValue<string>()).ToHashSet();
        var must=profile.Str("mustDo");
        if(Known(must))foreach(var term in Regex.Split(must,@"[，,、；;]|以及|还有|和|及").Where(Known)) {
            var matches=places.Where(p=>Matches(p,term)).ToArray();
            if(matches.Length>1 && matches.All(p=>RouteService.Distance(matches[0],p)<200))matches=[matches.FirstOrDefault(PoiService.IsImported)??matches[0]];
            if(matches.Length==0) {
                var theme=Regex.IsMatch(term,"当地美食|本地美食|尝.*美食|吃.*美食|food|local cuisine",RegexOptions.IgnoreCase)?"美食":
                    Regex.IsMatch(term,"看看?海|海边散步|港湾风景|看风景|sightseeing",RegexOptions.IgnoreCase)?"风景":
                    Regex.IsMatch(term,"亲近自然|逛公园|nature",RegexOptions.IgnoreCase)?"自然":"";
                if(theme!="") {var interests=profile.Array("interests")!;if(!interests.Any(i=>i!.GetValue<string>()==theme))interests.Add(theme);continue;}
            }
            if(matches.Length!=1)throw new ServiceException("Cannot yet match your must-see request, "+term+", to a place. Use a specific place name before generating.");
            required.Add(matches[0].Str("id"));
        }
        var excluded=new HashSet<string>();var avoid=profile.Str("avoid");
        if(Known(avoid))foreach(var raw in Regex.Split(avoid,@"[，,、；;]|以及|还有|和|及").Where(Known)) {
            if(Regex.IsMatch(raw,"不(?:用|必|需要)避开|不排除|可以去|无障碍|轮椅|階梯|台阶|楼梯|过敏"))throw new ServiceException("Your travel requirement, "+raw+", cannot be verified automatically. Specify a place or category to avoid.");
            if(Regex.IsMatch(raw,"休息|长时间步行|少走|多走路")){profile["maxSteps"]=Math.Min(profile.Int("maxSteps"),5000);continue;}
            var term=Regex.Replace(raw,@"^(必须|需要|这次|我|不想|不要|避免|避开|不去|不逛|逛|去|参观)+","").Trim();
            var matches=places.Where(p=>Matches(p,term) || (term=="博物馆" && (p.Str("name").Contains("博物馆") || p.Str("query").Contains("Museum",StringComparison.OrdinalIgnoreCase))) || (term==p.Str("category"))).ToArray();
            if(matches.Length==0)throw new ServiceException("Cannot yet verify your avoid request, "+raw+". Use a specific place or category before generating.");
            excluded.UnionWith(matches.Select(p=>p.Str("id")));
        }
        if(required.Overlaps(excluded))throw new ServiceException("The same place is both required and excluded. Please update this trip's requirements.");
        profile["mustVisit"]=new JsonArray(required.Select(id=>JsonValue.Create(id)).ToArray());
        var uncertainEvents=places.Where(SeasonalEventGuard.NeedsVerifiedDates).ToArray();
        var requiredEvent=uncertainEvents.FirstOrDefault(p=>required.Contains(p.Str("id")));
        if(requiredEvent is not null)throw new ServiceException(requiredEvent.Str("name")+" may be seasonal or limited-time, and availability for your dates is unverified, so it cannot be scheduled automatically. Your request is kept. Remove it as a required stop and add it manually after confirming the dates.");
        var filtered=places.Where(p=>!excluded.Contains(p.Str("id")) && !SeasonalEventGuard.NeedsVerifiedDates(p)).ToArray();
        if(filtered.Length==0 && uncertainEvents.Length>0)throw new ServiceException("These activities' dates are unverified, so a route cannot be generated yet. Choose permanent attractions instead. Your travel requirements will be kept.");
        if(filtered.Length==0)throw new ServiceException("No places remain after applying your exclusions. Please adjust your requirements.");
        return filtered;
    }
}
