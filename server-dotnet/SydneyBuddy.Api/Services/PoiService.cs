using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
namespace SydneyBuddy.Api;
public interface IPoiProvider {
    string Source=>"OpenStreetMap / Overpass";
    string Attribution=>"© OpenStreetMap contributors · ODbL";
    bool UsesRemoteService=>true;
    JsonObject Metadata=>new();
    Task<JsonArray> SearchAsync(double lat,double lng,int radius,string category,string text,CancellationToken token);}
public sealed class OsmPoiProvider(HttpClient http,IConfiguration? configuration=null):IPoiProvider {
    public static string Query(double lat,double lng,int radius,string category,string text) {
        var around=FormattableString.Invariant($"(around:{radius},{lat},{lng})");
        var name=text.Length==0?"":"[\"name\"~"+System.Text.Json.JsonSerializer.Serialize(Regex.Escape(text),new System.Text.Json.JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping})+",i]";
        var filters=category switch {"food"=>new[]{"[amenity~\"^(restaurant|cafe|fast_food|food_court)$\"]"},"facilities"=>new[]{"[amenity~\"^(toilets|drinking_water)$\"]"},_=>new[]{"[tourism~\"^(attraction|museum|gallery|viewpoint)$\"]","[leisure=park]","[historic]"}};
        return "[out:json][timeout:20];("+string.Concat(filters.Select(f=>"nwr"+around+f+name+";"))+");out center 120;";
    }
    public async Task<JsonArray> SearchAsync(double lat,double lng,int radius,string category,string text,CancellationToken token) {
        using var request=new HttpRequestMessage(HttpMethod.Post,configuration?["Poi:OverpassUrl"]??"https://overpass-api.de/api/interpreter");
        request.Headers.UserAgent.ParseAdd("SydneyBuddy/1.0 (+https://github.com/f1sh11/hackathon)");
        request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"data",Query(lat,lng,radius,category,text)}});
        try {
            using var response=await http.SendAsync(request,token);
            if(!response.IsSuccessStatusCode)throw new ServiceException("開源地點服務目前繁忙，請稍後手動重試。");
            var raw=JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonObject;
            if(raw is null||raw["remark"] is not null)throw new ServiceException("開源地點查詢未完成，請縮小範圍後重試。");
            var result=new JsonArray();
            foreach(var e in (raw.Array("elements")??new()).OfType<JsonObject>()) {
                var tags=e.Object("tags");if(tags is null)continue;
                var point=e.Object("center")??e;var a=point.Number("lat",double.NaN);var b=point.Number("lon",double.NaN);
                if(!ValidPoint(a,b))continue;
                var kind=tags.Str("amenity",tags.Str("tourism",tags.Str("leisure","attraction")));
                var name=tags.Str("name:zh",tags.Str("name",kind=="toilets"?"公共廁所":kind=="drinking_water"?"飲水設施":""));if(name.Length==0)continue;
                result.Add(new JsonObject { ["id"]="osm:"+e.Str("type")+":"+e["id"]?.ToString(),["name"]=name,["lat"]=a,["lng"]=b,["category"]=category=="food"?"美食":category=="facilities"?"設施":"風景",["indoor"]=tags.Str("indoor")=="yes",["visitMinutes"]=category=="food"?45:category=="facilities"?10:60,["cost"]=0,["source"]="OpenStreetMap",["poiKind"]=kind,["description"]=tags.Str("description:zh",tags.Str("description","OSM 社群地點，詳細介紹尚未提供。")),["openingHoursText"]=tags.Str("opening_hours"),["note"]="費用、營業狀態與排隊未知；尚未預留費用，請自行確認。",["reason"]="由地圖搜尋加入工具箱" });
            }
            return result;
        }catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new ServiceException("開源地點服務逾時，請稍後重試。");}
        catch(HttpRequestException){throw new ServiceException("無法連線開源地點服務，請稍後重試。");}
        catch(System.Text.Json.JsonException){throw new ServiceException("開源地點回應無效，請稍後重試。");}
    }
    internal static bool ValidPoint(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&a is >= -34.2 and <= -33.5&&b is >=150.5 and <=151.5;
}
public sealed class PoiService(IPoiProvider provider,IMemoryCache cache,GooglePoiProvider? google=null) {
    static readonly SemaphoreSlim Gate=new(1,1);
    public async Task<JsonObject> SearchAsync(JsonObject input,CancellationToken token) {
        var lat=input.Number("lat",-33.865);var lng=input.Number("lng",151.212);var radius=input.Int("radius",1500);var text=input.Str("query").Trim();var category=input.Str("category","sights");
        if(!OsmPoiProvider.ValidPoint(lat,lng)||radius is <100 or >5000||text.Length>80||category is not("sights" or "food" or "facilities"))throw new ServiceException("請使用悉尼範圍、100–5000 公尺及有效分類查詢。");
        var source=input.Str("provider","osm");
        if(source=="google")return google is null?throw new ServiceException("Google Places 尚未設定。"):
            await google.SearchResultAsync(lat,lng,radius,category,text,input.Str("language","zh-TW"),token);
        if(source!="osm")throw new ServiceException("不支援此地點來源；Apple Maps 請使用 iOS 原生搜尋。");
        if(!provider.UsesRemoteService)return Result(await provider.SearchAsync(lat,lng,radius,category,text,token));
        var key=FormattableString.Invariant($"poi:{lat:F3}:{lng:F3}:{radius}:{category}:{text}");
        if(cache.TryGetValue(key,out JsonArray? stored))return Result((JsonArray)stored!.DeepClone());
        await Gate.WaitAsync(token);
        try {
            if(cache.TryGetValue(key,out stored))return Result((JsonArray)stored!.DeepClone());
            if(cache.TryGetValue("poi:cooldown",out bool _))throw new ServiceException("請等待幾秒再搜尋，避免重複查詢開源服務。");
            cache.Set("poi:cooldown",true,new MemoryCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromSeconds(3),Size=1});
            JsonArray points;
            try {points=await provider.SearchAsync(lat,lng,radius,category,text,token);}
            catch(ServiceException){cache.Set("poi:cooldown",true,new MemoryCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromSeconds(30),Size=1});throw;}
            cache.Set(key,(JsonArray)points.DeepClone(),new MemoryCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(15),Size=1});return Result(points);
        }finally{Gate.Release();}
    }
    JsonObject Result(JsonArray points)=>new(){["places"]=points,["source"]=provider.Source,["attribution"]=provider.Attribution,["metadata"]=provider.Metadata,["message"]="開放地點資料快照；不包含即時評價、營業確認或價格。"};
    public static bool IsImported(JsonObject p)=>p.Str("id").StartsWith("osm:")||p.Str("id").StartsWith("apple:")||p.Str("id").StartsWith("overture:");
    public static string ActivityKind(JsonObject p)=>p.Str("poiKind")=="toilets"?"restroom":p.Str("poiKind")=="drinking_water"?"rest":p.Str("category")=="美食"?"meal":"attraction";
    public static JsonObject Import(JsonObject p) {
        var id=p.Str("id");var name=p.Str("name");var lat=p.Number("lat",double.NaN);var lng=p.Number("lng",double.NaN);
        var apple=p.Str("source")=="Apple Maps"&&Regex.IsMatch(id,@"^apple:[A-Za-z0-9_-]{1,180}$");
        var overture=p.Str("source")=="Overture Maps"&&id.StartsWith("overture:")&&Guid.TryParse(id[9..],out _);
        if(!(apple||overture||Regex.IsMatch(id,@"^osm:(node|way|relation):[0-9]{1,20}$"))||name.Length is <1 or >200||!OsmPoiProvider.ValidPoint(lat,lng))throw new ServiceException("工具箱地點資料無效。");
        string Short(string key,int max){var s=p.Str(key);return s[..Math.Min(max,s.Length)];}
        return new JsonObject{["id"]=id,["name"]=name,["lat"]=lat,["lng"]=lng,["source"]=apple?"Apple Maps":overture?"Overture Maps":"OpenStreetMap",["category"]=p.Str("category") is "美食"?"美食":p.Str("category") is "設施"?"設施":"風景",["indoor"]=p.Bool("indoor"),["visitMinutes"]=Math.Clamp(p.Int("visitMinutes",60),5,240),["cost"]=0,["description"]=Short("description",3000),["note"]="費用、營業狀態與排隊未知；尚未預留費用，請自行確認。",["website"]=Short("website",2000),["phone"]=Short("phone",100),["address"]=Short("address",600),["sourceRelease"]=Short("sourceRelease",40),["openingHoursText"]=Short("openingHoursText",300),["poiKind"]=Short("poiKind",40),["reason"]="由地圖搜尋加入工具箱"};
    }
}
