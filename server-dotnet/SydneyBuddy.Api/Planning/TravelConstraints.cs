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
            if(matches.Length!=1)throw new ServiceException("还不能确认必去要求「"+term+"」对应的地点，请改为明确的景点名称后再生成。");
            required.Add(matches[0].Str("id"));
        }
        var excluded=new HashSet<string>();var avoid=profile.Str("avoid");
        if(Known(avoid))foreach(var raw in Regex.Split(avoid,@"[，,、；;]|以及|还有|和|及").Where(Known)) {
            if(Regex.IsMatch(raw,"不(?:用|必|需要)避开|不排除|可以去|无障碍|轮椅|階梯|台阶|楼梯|过敏"))throw new ServiceException("出行要求「"+raw+"」还无法自动验证，请先确认可执行的避开地点或类别。");
            if(Regex.IsMatch(raw,"休息|长时间步行|少走|多走路")){profile["maxSteps"]=Math.Min(profile.Int("maxSteps"),5000);continue;}
            var term=Regex.Replace(raw,@"^(必须|需要|这次|我|不想|不要|避免|避开|不去|不逛|逛|去|参观)+","").Trim();
            var matches=places.Where(p=>Matches(p,term) || (term=="博物馆" && (p.Str("name").Contains("博物馆") || p.Str("query").Contains("Museum",StringComparison.OrdinalIgnoreCase))) || (term==p.Str("category"))).ToArray();
            if(matches.Length==0)throw new ServiceException("还不能自动核实避开要求「"+raw+"」，请改为明确的地点或类别后再生成。");
            excluded.UnionWith(matches.Select(p=>p.Str("id")));
        }
        if(required.Overlaps(excluded))throw new ServiceException("同一个地点同时被设为必去和避开，请先修改这次的需求。");
        profile["mustVisit"]=new JsonArray(required.Select(id=>JsonValue.Create(id)).ToArray());
        var uncertainEvents=places.Where(SeasonalEventGuard.NeedsVerifiedDates).ToArray();
        var requiredEvent=uncertainEvents.FirstOrDefault(p=>required.Contains(p.Str("id")));
        if(requiredEvent is not null)throw new ServiceException("「"+requiredEvent.Str("name")+"」可能是季节性或限时活动，尚未核实本次日期是否开放，暂不自动安排。需求已保留，请先取消这项必去要求，确认活动日期后再手动加入。");
        var filtered=places.Where(p=>!excluded.Contains(p.Str("id")) && !SeasonalEventGuard.NeedsVerifiedDates(p)).ToArray();
        if(filtered.Length==0 && uncertainEvents.Length>0)throw new ServiceException("这些候选活动的开放日期尚未核实，暂时不能生成路线。请选择其他常设地点，旅行需求会保留。");
        if(filtered.Length==0)throw new ServiceException("避开这些地点后暂时没有可规划的景点，请调整需求。");
        return filtered;
    }
}
