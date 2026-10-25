using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>Reference activity products remain independent of planner costs and restaurant prices.</summary>
public sealed class ActivityOfferService(ProjectPaths paths,TravelDataContext travel,ITravelWarehouse warehouse) {
    public async Task<JsonObject> SearchAsync(JsonObject input,CancellationToken token){
        var lat=input.Number("lat",double.NaN);var lng=input.Number("lng",double.NaN);var radius=input.Int("radius",1000);var query=input.Str("query").Trim();
        if(!double.IsFinite(lat)||!double.IsFinite(lng)||lat is < -90 or >90||lng is < -180 or >180||radius is <100 or >5000||query.Length>100)throw new ServiceException("活動搜尋範圍無效。");
        var placeId=input.Str("placeId");var placeName=input.Str("placeName");
        if(placeId.Length>250||placeName.Length>300)throw new ServiceException("景點識別資料無效。");
        var rows=travel.Warehouse?await warehouse.ActivityOffersAsync(token):JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(paths.RepoRoot,"shared","activity-offers.json"),token))!.AsObject().Array("offers")!;
        return Search(rows,lat,lng,radius,query,travel.Mode,DateTimeOffset.UtcNow,placeId,placeName);
    }
    public static JsonObject Search(JsonArray rows,double lat,double lng,int radius,string query,string mode,DateTimeOffset now,string placeId="",string placeName=""){
        var origin=new JsonObject{["lat"]=lat,["lng"]=lng};var found=new List<JsonObject>();
        foreach(var row in rows.OfType<JsonObject>()){
            var value=Validate(row);var distance=RouteService.Distance(origin,value);
            if(!MatchesVenue(value,placeId,placeName,distance)||distance>radius||query.Length>0&&CultureInfo.InvariantCulture.CompareInfo.IndexOf(value.Str("title")+" "+value.Str("venueName"),query,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)<0)continue;
            var observed=DateOnly.ParseExact(value.Str("observedOn"),"yyyy-MM-dd",CultureInfo.InvariantCulture);
            var through=DateOnly.ParseExact(value.Str("reviewBy"),"yyyy-MM-dd",CultureInfo.InvariantCulture);
            value["stale"]=DateOnly.FromDateTime(now.UtcDateTime).DayNumber-observed.DayNumber>30||DateOnly.FromDateTime(now.UtcDateTime)>through;
            value["distanceMeters"]=Math.Round(distance);value["budgetEligible"]=false;found.Add(value);
        }
        return new(){["dataSource"]=mode,["offers"]=new JsonArray(found.OrderBy(r=>r.Number("distanceMeters")).ThenBy(r=>r.Str("id")).Take(30).Select(r=>(JsonNode)r).ToArray()),["note"]="只顯示已配對此景點的活動；票種、日期與名額以官網為準，不自動扣預算。"};
    }
    static string VenueKey(string name)=>System.Text.RegularExpressions.Regex.Replace(name.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant(),@"[^\p{L}\p{N}]","");
    public static bool MatchesVenue(JsonObject offer,string placeId,string placeName,double distance){
        // Nearby is not an identity match. Even an explicit alias must share the venue location.
        if(distance>250||string.IsNullOrWhiteSpace(placeId)||string.IsNullOrWhiteSpace(placeName))return false;
        if((offer.Array("venueIds")??new()).Any(id=>id?.ToString()==placeId))return true;
        var name=VenueKey(placeName);if(name.Length==0)return false;
        return new[]{offer.Str("venueName")}.Concat((offer.Array("venueAliases")??new()).Select(alias=>alias?.ToString()??""))
            .Any(alias=>VenueKey(alias)==name);
    }
    public static JsonObject Validate(JsonObject row){
        var price=row.Number("amount",double.NaN);var lat=row.Number("lat",double.NaN);var lng=row.Number("lng",double.NaN);
        if(row.Str("id").Length is <1 or >150||row.Str("title").Length is <1 or >300||row.Str("venueName").Length is <1 or >200||row.Str("ticketType").Length is <1 or >200||row.Str("sourceName").Length is <1 or >200||row.Str("conditions").Length is <1 or >2000||!double.IsFinite(price)||price<0||price>100000||!double.IsFinite(lat)||!double.IsFinite(lng)||lat is < -90 or >90||lng is < -180 or >180||!System.Text.RegularExpressions.Regex.IsMatch(row.Str("currency"),"^[A-Z]{3}$")||!Uri.TryCreate(row.Str("sourceUrl"),UriKind.Absolute,out var url)||url.Scheme!="https"||url.UserInfo.Length>0||!DateOnly.TryParseExact(row.Str("observedOn"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var observed)||!DateOnly.TryParseExact(row.Str("reviewBy"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var through)||through<observed)throw new ServiceException("活動參考資料格式無效。");
        var value=row.DeepClone().AsObject();value["priceType"]="reference_ticket_price";value["budgetEligible"]=false;return value;
    }
}
