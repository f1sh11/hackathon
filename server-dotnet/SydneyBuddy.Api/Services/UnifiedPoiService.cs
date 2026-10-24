using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>One discovery contract for Overture, OSM and attributed community guides.</summary>
public sealed class UnifiedPoiService(TravelDataContext travel,OverturePoiProvider overture,HiddenGemService gems,ProjectPaths paths,SocialImportStore? social=null) {
    public JsonObject Search(double lat,double lng,int radius,string category,string text,CancellationToken token){
        JsonObject original;JsonArray osm,community;
        if(travel.Warehouse){var snapshot=travel.Snapshot??throw new ServiceException("Zetaris 地圖資料尚未取得。");original=snapshot.Poi;osm=snapshot.Osm??throw new ServiceException("Zetaris OSM 地點尚未匯入。");community=snapshot.Gems??new();}
        else {original=overture.PublicSnapshot;community=gems.PublicPlaces;var path=Path.Combine(paths.RepoRoot,"infra","zetaris","data","osm-map-places.json");
            if(!File.Exists(path))throw new ServiceException("整合地圖資料尚未匯入，請先執行 scripts/import_osm_map_places.py。");
            osm=JsonNode.Parse(File.ReadAllText(path))!.AsObject().Array("places")??throw new ServiceException("OSM 地點資料無效。");}
        var socialPoints=travel.Warehouse ? travel.Snapshot?.Social ?? new JsonArray() : social?.Published() ?? new JsonArray();
        var result=Merge(original.Array("places")!.OfType<JsonObject>().Concat(osm.OfType<JsonObject>()).Concat(community.OfType<JsonObject>()).Concat(socialPoints.OfType<JsonObject>()),lat,lng,radius,category,text,token);
        result["source"]=travel.Warehouse?"Open travel data / Zetaris":"Open travel data / local";result["dataSource"]=travel.Mode;
        result["attribution"]="© OpenStreetMap contributors · ODbL; Overture Maps Foundation · CDLA/Apache; Wikivoyage contributors · CC BY-SA 4.0; social recommendations retain their source attribution";
        result["metadata"]=new JsonObject{["release"]=original.Str("release"),["readAt"]=travel.Snapshot?.ReadAt,["totalPlaces"]=original.Array("places")!.Count+osm.Count+community.Count+socialPoints.Count};
        result["message"]="整合已匯入的雪梨地點；底圖原生標記可能不同。探索候選不代表已確認冷門；房價、營業狀態與評論未即時核實；社群推薦為附原文證據的作者意見。";
        return result;
    }
    static string NameKey(string value)=>string.Concat(value.Normalize(NormalizationForm.FormD).Where(c=>char.IsLetterOrDigit(c)&&CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    static string Kind(JsonObject p)=>p.Bool("hiddenGem")||p.Str("source")=="Wikivoyage"?"gems":p.Str("searchCategory",p.Str("category") switch{"美食"=>"food","設施"=>"facilities","住宿"=>"lodging","店家"=>"shops",_=>"sights"});
    static int Richness(JsonObject p)=>(p.Array("socialRecommendations") is null?0:1000)+(p.Object("guide") is null?0:100)+(p.Array("photos")?.Count??0)*5+(p.Str("source")=="OpenStreetMap"?2:0);
    public static JsonObject Merge(IEnumerable<JsonObject> points,double lat,double lng,int radius,string category,string text,CancellationToken token){
        var origin=new JsonObject{["lat"]=lat,["lng"]=lng};var tokens=text.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var groups=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal);var identifiers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var point in points.OrderByDescending(Richness)){
            token.ThrowIfCancellationRequested();var kind=Kind(point);
            if(!PoiCategories.Matches(point,category))continue;
            if(!tokens.All(q=>CultureInfo.InvariantCulture.CompareInfo.IndexOf(point.Str("searchText",point.Str("name")+" "+point.Str("description")+" "+point.Str("address")),q,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0))continue;
            if(!identifiers.Add(point.Str("id")))continue;
            var meters=RouteService.Distance(origin,point);if(meters>radius)continue;
            var name=NameKey(point.Str("name"))+":"+point.Str("category");if(!groups.TryGetValue(name,out var group))groups[name]=group=[];
            // Same name alone is insufficient: branches can be near each other.
            var existing=group.FirstOrDefault(p=>RouteService.Distance(p,point)<=25);
            if(existing is not null){
                var sources=existing.Array("sources")!;if(!sources.Any(s=>s?.ToString()==point.Str("source")))sources.Add(point.Str("source"));existing.Array("sourceIds")!.Add(point.Str("id"));
                foreach(var field in new[]{"website","phone","address","openingHoursText","wikidata"})if(existing.Str(field).Length==0&&point.Str(field).Length>0)existing[field]=point[field]!.DeepClone();
                continue;
            }
            var value=point.DeepClone().AsObject();value["distanceMeters"]=Math.Round(meters);value["sources"]=new JsonArray(point.Str("source"));value["sourceIds"]=new JsonArray(point.Str("id"));value["searchCategory"]=kind;group.Add(value);
        }
        var ordered=groups.Values.SelectMany(x=>x).OrderBy(p=>p.Number("distanceMeters")).ThenBy(p=>p.Str("id")).ToList();
        // Reserve slots for each kind so dense cafes don't hide hotels or community places.
        var chosen=category=="all"&&text.Length==0?ordered.GroupBy(Kind).SelectMany(g=>g.Take(13)).OrderBy(p=>p.Number("distanceMeters")).Take(80).ToList():ordered.Take(80).ToList();
        var result=new JsonArray();foreach(var point in chosen){point.Remove("searchText");point.Remove("searchCategory");result.Add(point);}
        return new(){["places"]=result,["totalMatches"]=ordered.Count,["returnedCount"]=result.Count,["limit"]=80,["truncated"]=ordered.Count>result.Count};
    }
}
