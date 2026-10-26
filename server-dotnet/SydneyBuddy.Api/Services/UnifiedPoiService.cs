using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>One discovery contract for Overture, OSM and attributed community guides.</summary>
public sealed class UnifiedPoiService(TravelDataContext travel,OverturePoiProvider overture,HiddenGemService gems,ProjectPaths paths,PlaceCatalog? catalog=null,SocialImportStore? social=null,RestaurantStore? restaurants=null) {
    public JsonObject Search(double lat,double lng,int radius,string category,string text,CancellationToken token){
        JsonObject original;JsonArray osm,community,catalogPoints;
        if(travel.Warehouse){
            var snapshot=travel.Snapshot??throw new ServiceException("Zetaris 地圖資料尚未取得。");
            original=snapshot.Poi;
            osm=snapshot.Osm??throw new ServiceException("Zetaris OSM 地點尚未匯入。");
            community=snapshot.Gems??new();
            catalogPoints=snapshot.Catalog;
        }
        else {
            original=File.Exists(Path.Combine(paths.RepoRoot,"infra","poi","data","places.json")) ? overture.PublicSnapshot : new JsonObject{["places"]=new JsonArray()};
            try { community = gems?.PublicPlaces ?? new JsonArray(); } catch { community = new JsonArray(); }
            var path=Path.Combine(paths.RepoRoot,"infra","zetaris","data","osm-map-places.json");
            osm=File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject().Array("places")??new JsonArray() : new JsonArray();
            catalogPoints=catalog is not null ? new JsonArray(catalog.Get().Select(x=>(JsonNode)x.DeepClone()).ToArray()) : new JsonArray();
        }
        var socialPoints=travel.Warehouse ? travel.Snapshot?.Social ?? new JsonArray() : social?.Published() ?? new JsonArray();
        var restaurantPoints=travel.Warehouse ? travel.Snapshot?.Restaurants ?? new JsonArray() : restaurants?.Published() ?? new JsonArray();
        var allPoints=catalogPoints.OfType<JsonObject>().Concat(original.Array("places")!.OfType<JsonObject>()).Concat(osm.OfType<JsonObject>()).Concat(community.OfType<JsonObject>()).Concat(socialPoints.OfType<JsonObject>()).Concat(restaurantPoints.OfType<JsonObject>());
        var result=Merge(allPoints,lat,lng,radius,category,text,token);
        result["source"]=travel.Warehouse?"Open travel data / Zetaris":"Open travel data / local";result["dataSource"]=travel.Mode;
        result["attribution"]="© OpenStreetMap contributors · ODbL; Overture Maps Foundation · CDLA/Apache; Wikivoyage contributors · CC BY-SA 4.0; social recommendations retain their source attribution";
        result["metadata"]=new JsonObject{["release"]=original.Str("release"),["readAt"]=travel.Snapshot?.ReadAt,["totalPlaces"]=catalogPoints.Count+original.Array("places")!.Count+osm.Count+community.Count+socialPoints.Count+restaurantPoints.Count};
        result["message"]="整合已匯入的新南威爾斯州 (NSW) 地點；底圖原生標記可能不同。探索候選不代表已確認冷門；房價、營業狀態與評論未即時核實；社群推薦為附原文證據的作者意見。";
        return result;
    }
    static string NameKey(string value)=>string.Concat(value.Normalize(NormalizationForm.FormD).Where(c=>char.IsLetterOrDigit(c)&&CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    static string Kind(JsonObject p)=>p.Bool("hiddenGem")||p.Str("source")=="Wikivoyage"?"gems":p.Str("searchCategory",p.Str("category") switch{"美食"=>"food","設施"=>"facilities","住宿"=>"lodging","店家"=>"shops",_=>"sights"});
    static int Richness(JsonObject p)=>(p.Str("source")=="Restaurant evidence"?3000:0)+(p.Array("socialRecommendations") is null?0:1000)+(p.Str("source").Contains("catalog",StringComparison.OrdinalIgnoreCase)?2500:0)+(p.Object("guide") is null?0:100)+(p.Array("photos")?.Count??0)*5+(p.Str("source")=="OpenStreetMap"?2:0);
    sealed class Match(JsonObject point,double meters,string kind) {
        public JsonObject Point {get;}=point;
        public double Meters {get;}=meters;
        public string Kind {get;}=kind;
        public List<JsonObject> Duplicates {get;}=[];
    }
    public static JsonObject Merge(IEnumerable<JsonObject> points,double lat,double lng,int radius,string category,string text,CancellationToken token){
        var origin=new JsonObject{["lat"]=lat,["lng"]=lng};var tokens=text.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var groups=new Dictionary<string,List<Match>>(StringComparer.Ordinal);var identifiers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var point in points.OrderByDescending(Richness)){
            token.ThrowIfCancellationRequested();
            if(point.Str("source").Length==0)point["source"]="Catalog";
            if(point.Str("poiKind").Length==0)point["poiKind"]=point.Str("activityType")=="meal"?"restaurant":"attraction";
            // iOS ScheduleCandidate requires these keys; catalog rows may omit them.
            if(point["reason"] is null)point["reason"]=point.Str("note").Length>0?point.Str("note"):"精選地點";
            if(point["indoor"] is null)point["indoor"]=false;
            if(point["visitMinutes"] is null)point["visitMinutes"]=60;
            if(point["cost"] is null)point["cost"]=0;
            var kind=Kind(point);
            if(!PoiCategories.Matches(point,category))continue;
            if(!tokens.All(q=>CultureInfo.InvariantCulture.CompareInfo.IndexOf(point.Str("searchText",point.Str("name")+" "+point.Str("description")+" "+point.Str("address")),q,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0))continue;
            if(!identifiers.Add(point.Str("id")))continue;
            var meters=RouteService.Distance(origin,point);if(meters>radius)continue;
            var name=NameKey(point.Str("name"))+":"+point.Str("category");if(!groups.TryGetValue(name,out var group))groups[name]=group=[];
            // Same name alone is insufficient: branches can be near each other.
            var existing=group.FirstOrDefault(match=>RouteService.Distance(match.Point,point)<=25 && (match.Point.Str("source")!=point.Str("source") || match.Point.Str("id")==point.Str("id") || match.Point.Str("matchedPlaceId").Length>0&&match.Point.Str("matchedPlaceId")==point.Str("matchedPlaceId")));
            if(existing is not null){existing.Duplicates.Add(point);continue;}
            group.Add(new Match(point,Math.Round(meters),kind));
        }
        var ordered=groups.Values.SelectMany(x=>x).OrderBy(p=>p.Meters).ThenBy(p=>p.Point.Str("id")).ToList();
        // Keep references while filtering and ranking; clone only the bounded response.
        // Cloning every nearby POI exhausted the managed heap on the free Oracle VM.
        var chosen=category=="all"&&text.Length==0?ordered.GroupBy(p=>p.Kind).SelectMany(g=>g.Take(13)).OrderBy(p=>p.Meters).Take(80):ordered.Take(80);
        var result=new JsonArray();
        foreach(var match in chosen){
            var value=match.Point.DeepClone().AsObject();value["distanceMeters"]=match.Meters;
            value["sources"]=new JsonArray(match.Point.Str("source"));value["sourceIds"]=new JsonArray(match.Point.Str("id"));
            foreach(var point in match.Duplicates){
                var sources=value.Array("sources")!;if(!sources.Any(s=>s?.ToString()==point.Str("source")))sources.Add(point.Str("source"));value.Array("sourceIds")!.Add(point.Str("id"));
                foreach(var field in new[]{"website","phone","address","openingHoursText","wikidata"})if(value.Str(field).Length==0&&point.Str(field).Length>0)value[field]=point[field]!.DeepClone();
                if((value.Array("photos") is null||value.Array("photos")!.Count==0)&&point.Array("photos")?.Count>0)value["photos"]=point.Array("photos")!.DeepClone();
                if(value.Object("restaurantData") is JsonObject restaurant && point.Str("source")!="Restaurant evidence" && point.Object("restaurantData") is JsonObject communityRestaurant){
                    foreach(var field in new[]{"reportedSpends","recommendations","reviews"})foreach(var claim in communityRestaurant.Array(field)??new JsonArray()){
                        var entries=restaurant.Array(field)!;if(!entries.Any(x=>x!.Str("quote")==claim!.Str("quote")&&x.Object("source")?.Str("url")==claim.Object("source")?.Str("url")))entries.Add(claim!.DeepClone());
                    }
                }
                if(point.Array("socialRecommendations") is JsonArray socialClaims){
                    if(value.Array("socialRecommendations") is null)value["socialRecommendations"]=new JsonArray();
                    foreach(var claim in socialClaims){var target=value.Array("socialRecommendations")!;if(!target.Any(x=>x!.Str("sourceUrl")==claim!.Str("sourceUrl")))target.Add(claim!.DeepClone());}
                }
            }
            PoiCategories.ApplyFoodPriceMapping(value);value.Remove("searchText");value.Remove("searchCategory");result.Add(value);
        }
        return new(){["places"]=result,["totalMatches"]=ordered.Count,["returnedCount"]=result.Count,["limit"]=80,["truncated"]=ordered.Count>result.Count};
    }
}
