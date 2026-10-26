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
        var filters=category switch {
            "restaurants"=>new[]{"[amenity~\"^(restaurant|fast_food|food_court)$\"]"},
            "cafes"=>new[]{"[amenity=cafe]"},"bakeries"=>new[]{"[shop~\"^(bakery|pastry)$\"]"},
            "bars"=>new[]{"[amenity~\"^(bar|pub|nightclub|biergarten)$\"]"},
            "museums"=>new[]{"[tourism=museum]"},"parks"=>new[]{"[leisure~\"^(park|garden)$\"]"},"beaches"=>new[]{"[natural=beach]"},
            "hotels"=>new[]{"[tourism~\"^(hotel|motel|guest_house)$\"]"},"hostels"=>new[]{"[tourism=hostel]"},
            "supermarkets"=>new[]{"[shop~\"^(supermarket|convenience)$\"]"},
            "toilets"=>new[]{"[amenity=toilets]"},"drinking_water"=>new[]{"[amenity=drinking_water]"},"all"=>new[]{"[tourism]","[shop]","[historic]","[amenity~\"^(restaurant|cafe|fast_food|food_court|toilets|drinking_water)$\"]"},"lodging"=>new[]{"[tourism~\"^(hotel|hostel|motel|guest_house)$\"]"},"shops"=>new[]{"[shop]"},"gems"=>new[]{"[tourism=viewpoint]","[historic]","[waterway=waterfall]"},"food"=>new[]{"[amenity~\"^(restaurant|cafe|fast_food|food_court)$\"]"},"facilities"=>new[]{"[amenity~\"^(toilets|drinking_water)$\"]"},_=>new[]{"[tourism~\"^(attraction|museum|gallery|viewpoint)$\"]","[leisure=park]","[historic]"}};
        return "[out:json][timeout:20];("+string.Concat(filters.Select(f=>"nwr"+around+f+name+";"))+");out center 120;";
    }
    public async Task<JsonArray> SearchAsync(double lat,double lng,int radius,string category,string text,CancellationToken token) {
        using var request=new HttpRequestMessage(HttpMethod.Post,configuration?["Poi:OverpassUrl"]??"https://overpass-api.de/api/interpreter");
        request.Headers.UserAgent.ParseAdd("SydneyBuddy/1.0 (+https://github.com/f1sh11/hackathon)");
        request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"data",Query(lat,lng,radius,category,text)}});
        try {
            using var response=await http.SendAsync(request,token);
            if(!response.IsSuccessStatusCode)throw new ServiceException("The open place service is busy. Please try again later.");
            var raw=JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonObject;
            if(raw is null||raw["remark"] is not null)throw new ServiceException("The open place query did not complete. Reduce the search area and try again.");
            var result=new JsonArray();
            foreach(var e in (raw.Array("elements")??new()).OfType<JsonObject>()) {
                var tags=e.Object("tags");if(tags is null||tags.Str("access") is "private" or "no")continue;
                var point=e.Object("center")??e;var a=point.Number("lat",double.NaN);var b=point.Number("lon",double.NaN);
                if(!ValidPoint(a,b))continue;
                var kind=tags.Str("amenity",tags.Str("tourism",tags.Str("leisure",tags.Str("shop",tags.Str("natural","attraction")))));
                var name=tags.Str("name:en",tags.Str("name",kind=="toilets"?"Public toilet":kind=="drinking_water"?"Drinking water":""));if(name.Length==0)continue;
                var kindCategory=category=="all"?(tags.Str("shop").Length>0?"shops":new[]{"hotel","hostel","motel","guest_house"}.Contains(tags.Str("tourism"))?"lodging":new[]{"restaurant","cafe","fast_food","food_court"}.Contains(tags.Str("amenity"))?"food":new[]{"toilets","drinking_water"}.Contains(tags.Str("amenity"))?"facilities":"sights"):PoiCategories.Parent(category);
                var item=new JsonObject { ["id"]="osm:"+e.Str("type")+":"+e["id"]?.ToString(),["name"]=name,["lat"]=a,["lng"]=b,["category"]=kindCategory=="food"?"美食":kindCategory=="facilities"?"設施":kindCategory=="lodging"?"住宿":kindCategory=="shops"?"店家":"風景",["indoor"]=tags.Str("indoor")=="yes",["visitMinutes"]=kindCategory=="food"?45:kindCategory=="facilities"?10:60,["cost"]=0,["source"]="OpenStreetMap",["poiKind"]=kind,["description"]=tags.Str("description:en",tags.Str("description","OSM community place. A detailed description is not available.")),["openingHoursText"]=tags.Str("opening_hours"),["note"]="Costs, opening status and queues are unknown. No cost has been reserved. Please check before visiting.",["reason"]="Added to your toolbox from map search" };
                PoiCategories.ApplyFoodPriceMapping(item);
                result.Add(item);
            }
            return result;
        }catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new ServiceException("The open place service timed out. Please try again later.");}
        catch(HttpRequestException){throw new ServiceException("Could not connect to the open place service. Please try again later.");}
        catch(System.Text.Json.JsonException){throw new ServiceException("The open place response is invalid. Please try again later.");}
    }
    internal static bool ValidPoint(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&a is >= -34.2 and <= -33.5&&b is >=150.5 and <=151.5;
}
public sealed class PoiService(IPoiProvider provider,IMemoryCache cache,GooglePoiProvider? google=null,TravelDataContext? travel=null,HiddenGemService? gems=null,UnifiedPoiService? unified=null) {
    static readonly SemaphoreSlim Gate=new(1,1);
    public async Task<JsonObject> SearchAsync(JsonObject input,CancellationToken token) {
        var lat=input.Number("lat",-33.865);var lng=input.Number("lng",151.212);var radius=input.Int("radius",1500);var text=input.Str("query").Trim();var category=input.Str("category","sights");
        if(!OsmPoiProvider.ValidPoint(lat,lng)||radius is <100 or >5000||text.Length>80||!PoiCategories.Valid(category))throw new ServiceException("Search within Sydney using a radius of 100–5000 metres and a valid category.");
        if(unified is not null&&(travel?.Warehouse==true||input.Str("provider","osm")=="osm"&&provider is OverturePoiProvider||category=="gems"))return unified.Search(lat,lng,radius,category,text,token);
        if(category=="gems")return gems?.Search(lat,lng,radius,text,token)??throw new ServiceException("The hidden gem source is not configured.");
        if(travel?.Warehouse==true){
            var snapshot=travel.Snapshot??throw new ServiceException("Zetaris business data is not available yet.");
            var found=OverturePoiProvider.SearchSnapshot(snapshot.Poi,lat,lng,radius,category,text,token);
            return new JsonObject{["places"]=found,["source"]="Overture Maps / Zetaris",["dataSource"]=travel.Mode,["attribution"]="Overture Maps Foundation · CDLA-Permissive-2.0 / Apache-2.0",["metadata"]=new JsonObject{["release"]=snapshot.Poi.Str("release"),["readAt"]=snapshot.ReadAt,["totalPlaces"]=snapshot.Poi.Array("places")!.Count},["message"]="Zetaris public data snapshot. Business information is not live."};
        }
        var source=input.Str("provider","osm");
        if(source=="google")return google is null?throw new ServiceException("Google Places is not configured."):
            await google.SearchResultAsync(lat,lng,radius,category,text,"en",token);
        if(source!="osm")throw new ServiceException("This place source is not supported. Use native iOS search for Apple Maps.");
        if(!provider.UsesRemoteService)return Result(await provider.SearchAsync(lat,lng,radius,category,text,token));
        var key=FormattableString.Invariant($"poi:{lat:F3}:{lng:F3}:{radius}:{category}:{text}");
        if(cache.TryGetValue(key,out JsonArray? stored))return Result((JsonArray)stored!.DeepClone());
        await Gate.WaitAsync(token);
        try {
            if(cache.TryGetValue(key,out stored))return Result((JsonArray)stored!.DeepClone());
            if(cache.TryGetValue("poi:cooldown",out bool _))throw new ServiceException("Wait a few seconds before searching again to avoid duplicate requests to the open service.");
            cache.Set("poi:cooldown",true,new MemoryCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromSeconds(3),Size=1});
            JsonArray points;
            try {points=await provider.SearchAsync(lat,lng,radius,category,text,token);}
            catch(ServiceException){cache.Set("poi:cooldown",true,new MemoryCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromSeconds(30),Size=1});throw;}
            cache.Set(key,(JsonArray)points.DeepClone(),new MemoryCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(15),Size=1});return Result(points);
        }finally{Gate.Release();}
    }
    JsonObject Result(JsonArray points)=>new(){["places"]=points,["source"]=provider.Source,["attribution"]=provider.Attribution,["metadata"]=provider.Metadata,["message"]="Open place data snapshot. Live reviews, verified opening status and prices are not included."};
    public static bool IsImported(JsonObject p)=>p.Str("id").StartsWith("restaurant:")||p.Str("id").StartsWith("social:")||p.Str("id").StartsWith("gem:")||p.Str("id").StartsWith("osm:")||p.Str("id").StartsWith("apple:")||p.Str("id").StartsWith("overture:");
    public static string ActivityKind(JsonObject p)=>p.Str("poiKind")=="toilets"?"restroom":p.Str("poiKind")=="drinking_water"?"rest":p.Str("category")=="美食"?"meal":"attraction";
    public static JsonObject Import(JsonObject p) {
        var id=p.Str("id");var name=p.Str("name");var lat=p.Number("lat",double.NaN);var lng=p.Number("lng",double.NaN);
        var restaurant=p.Str("source")=="Restaurant evidence"&&Regex.IsMatch(id,@"^restaurant:[a-f0-9]{24}$");
        var social=p.Str("source")=="Community social recommendation"&&Regex.IsMatch(id,@"^social:[a-f0-9]{24}:[0-9]$");
        var apple=p.Str("source")=="Apple Maps"&&Regex.IsMatch(id,@"^apple:[A-Za-z0-9_-]{1,180}$");
        var overture=p.Str("source")=="Overture Maps"&&id.StartsWith("overture:")&&Guid.TryParse(id[9..],out _);
        var gem=p.Str("source")=="Wikivoyage"&&Regex.IsMatch(id,@"^gem:[a-f0-9]{24}$");
        if(!(restaurant||social||gem||apple||overture||Regex.IsMatch(id,@"^osm:(node|way|relation):[0-9]{1,20}$"))||name.Length is <1 or >200||!OsmPoiProvider.ValidPoint(lat,lng))throw new ServiceException("The toolbox place data is invalid.");
        string Short(string key,int max){var s=p.Str(key);return s[..Math.Min(max,s.Length)];}
        var result=new JsonObject{["id"]=id,["name"]=name,["lat"]=lat,["lng"]=lng,["source"]=restaurant?"Restaurant evidence":social?"Community social recommendation":gem?"Wikivoyage":apple?"Apple Maps":overture?"Overture Maps":"OpenStreetMap",["category"]=p.Str("category") is "美食" or "設施" or "住宿" or "店家"?p.Str("category"):"風景",["indoor"]=p.Bool("indoor"),["visitMinutes"]=Math.Clamp(p.Int("visitMinutes",60),5,240),["cost"]=0,["description"]=Short("description",3000),["note"]="Costs, opening status and queues are unknown. No cost has been reserved. Please check before visiting.",["website"]=Short("website",2000),["phone"]=Short("phone",100),["address"]=Short("address",600),["sourceRelease"]=Short("sourceRelease",40),["openingHoursText"]=Short("openingHoursText",300),["poiKind"]=Short("poiKind",40),["reason"]="Added to your toolbox from map search"};
        PoiCategories.ApplyFoodPriceMapping(result);
        foreach(var key in new[]{"sourceUrl","sourceLicense","retrievedAt","sources","sourceIds","wikidata","hiddenGem","accessStatus","priceRange","priceTier","estimatedSpendAud","avgCostForTwoAud","nightlyRateAud","priceLabel","ratingSource","ratingSourceUrl","ratingStatus","aggregateRating","reviewCount","ratingLabel","reviewSummary","reviewHighlights","photos"})if(p[key] is not null&&result[key] is null)result[key]=p[key]!.DeepClone();
        PoiCategories.ApplyPlaceRatingMapping(result);
        if(gem||social||restaurant){foreach(var key in new[]{"guide","photos","reviews","hiddenGem","accessStatus","reviewsStatus","socialRecommendations","matchedPlaceId","verificationStatus","socialHiddenGemClaim","restaurantData"})if(p[key] is not null)result[key]=p[key]!.DeepClone();}
        return result;
    }
}
