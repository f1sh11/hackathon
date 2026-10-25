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
        var rows=await ReadRowsAsync(token);
        return Search(rows,lat,lng,radius,query,travel.Mode,DateTimeOffset.UtcNow,placeId,placeName);
    }
    async Task<JsonArray> ReadRowsAsync(CancellationToken token)=>travel.Warehouse?await warehouse.ActivityOffersAsync(token):JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(paths.RepoRoot,"shared","activity-offers.json"),token))!.AsObject().Array("offers")!;
    public async Task<JsonObject> SummariesAsync(JsonObject input,CancellationToken token){
        var points=input.Array("places")??throw new ServiceException("缺少景點清單。");
        if(points.Count>80||points.Any(p=>p is not JsonObject||p.Str("id").Length is <1 or >250||p.Str("name").Length is <1 or >300||!double.IsFinite(p.Number("lat",double.NaN))||!double.IsFinite(p.Number("lng",double.NaN))||Math.Abs(p.Number("lat"))>90||Math.Abs(p.Number("lng"))>180))throw new ServiceException("票價摘要景點格式無效。");
        var result=new JsonObject();if(points.Count==0)return new(){["dataSource"]=travel.Mode,["summaries"]=result};
        var rows=await ReadRowsAsync(token);var now=DateTimeOffset.UtcNow;
        foreach(var point in points.OfType<JsonObject>()){
            token.ThrowIfCancellationRequested();var offers=Search(rows,point.Number("lat"),point.Number("lng"),1000,"",travel.Mode,now,point.Str("id"),point.Str("name")).Array("offers")!;
            var summary=Summarize(offers);if(summary is not null)result[point.Str("id")]=summary;
        }
        return new(){["dataSource"]=travel.Mode,["summaries"]=result};
    }
    public static JsonObject? Summarize(JsonArray offers){
        var eligible=offers.OfType<JsonObject>().Where(o=>!o.Bool("stale")&&(o.Str("pricingStatus") is "known" or "free")&&(o.Str("audience") is "adult" or "general")&&o["amount"] is not null).ToList();
        // Prefer comparable admission prices, then standalone tours. Never average unlike products.
        foreach(var experience in new[]{"general_admission","guided_tour","combo"}){
            var group=eligible.Where(o=>o.Str("experience")==experience).ToList();if(group.Count==0)continue;
            if(group.Select(o=>o.Str("currency")).Distinct().Count()!=1)return null;
            var best=group.OrderBy(o=>o.Number("amount")).First();
            return new(){["amount"]=best["amount"]!.DeepClone(),["currency"]=best.Str("currency"),["basis"]=experience,["audience"]=best.Str("audience"),["offerId"]=best.Str("id"),["sourceUrl"]=best.Str("sourceUrl"),["observedOn"]=best.Str("observedOn"),["budgetEligible"]=false};
        }
        return null;
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
        var price=row.Number("amount",double.NaN);var status=row.Str("pricingStatus",row["amount"] is null ? "unknown":price==0 ? "free":"known");
        var validPrice=status=="unknown" ? row["amount"] is null:(status is "free" or "known")&&double.IsFinite(price)&&price>=0&&price<=100000&&(status!="free"||price==0)&&(status!="known"||price>0);
        var lat=row.Number("lat",double.NaN);var lng=row.Number("lng",double.NaN);
        if(row.Str("id").Length is <1 or >150||row.Str("title").Length is <1 or >300||row.Str("venueName").Length is <1 or >200||row.Str("ticketType").Length is <1 or >200||row.Str("sourceName").Length is <1 or >200||row.Str("conditions").Length is <1 or >2000||!validPrice||!double.IsFinite(lat)||!double.IsFinite(lng)||lat is < -90 or >90||lng is < -180 or >180||!System.Text.RegularExpressions.Regex.IsMatch(row.Str("currency"),"^[A-Z]{3}$")||!Uri.TryCreate(row.Str("sourceUrl"),UriKind.Absolute,out var url)||url.Scheme!="https"||url.UserInfo.Length>0||!DateOnly.TryParseExact(row.Str("observedOn"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var observed)||!DateOnly.TryParseExact(row.Str("reviewBy"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var through)||through<observed)throw new ServiceException("活動參考資料格式無效。");
        var value=row.DeepClone().AsObject();value["pricingStatus"]=status;value["priceType"]="reference_ticket_price";value["budgetEligible"]=false;return value;
    }
}
