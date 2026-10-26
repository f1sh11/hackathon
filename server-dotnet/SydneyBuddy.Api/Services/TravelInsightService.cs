using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace SydneyBuddy.Api;

/// <summary>Public, bounded insight feeds. No arbitrary URL/SQL or invented crowds/zero-cost defaults.</summary>
public sealed class TravelInsightService(HttpClient http,TravelDataContext selection,ITravelWarehouse warehouse,ProjectPaths paths,SecretStore secrets,PaidServicePolicy paid,IMemoryCache? cache=null) {
    async Task<JsonObject> Feed(string kind,CancellationToken token) {
        var key="travel-insights-v1:"+selection.Mode+":"+kind;
        if(cache is not null&&cache.TryGetValue<JsonObject>(key,out var hit))return hit!.DeepClone().AsObject();
        JsonObject raw;
        if(selection.Warehouse)raw=await warehouse.InsightRawAsync(kind,token);
        else {
            var sources=JsonNode.Parse(File.ReadAllText(Path.Combine(paths.RepoRoot,"shared","travel-insight-sources.json")))!.AsObject();
            using var response=await http.GetAsync(sources.Object(kind)!.Str("endpoint"),token);response.EnsureSuccessStatusCode();
            raw=await response.Content.ReadFromJsonAsync<JsonObject>(token)??throw new ServiceException("The public data format is invalid.");
        }
        if(raw.Array(kind=="traffic"?"features":"elements") is null)throw new ServiceException("The public dataset is incomplete.");
        raw["insightFetchedAt"]=DateTimeOffset.UtcNow.ToString("O");
        cache?.Set(key,raw.DeepClone().AsObject(),kind=="traffic"?TimeSpan.FromSeconds(30):TimeSpan.FromMinutes(10));
        return raw;
    }

    public static JsonArray Points(JsonObject input) {
        var points=input.Array("places")??new();if(points.Count>30)throw new ServiceException("You can query up to 30 itinerary places at once.");
        foreach(var p in points.OfType<JsonObject>())if(!OsmPoiProvider.ValidPoint(p.Number("lat",double.NaN),p.Number("lng",double.NaN))||p.Str("name").Length>160||p.Str("id").Length>200)throw new ServiceException("The place is invalid.");
        if(points.Any(p=>p is not JsonObject))throw new ServiceException("The place format is invalid.");
        return points;
    }
    public async Task<JsonObject> GetAsync(JsonObject input,CancellationToken token) {
        var points=Points(input);var started=DateTimeOffset.UtcNow;var result=new JsonObject{["dataSource"]=selection.Mode,["fetchedAt"]=started.ToString("O"),["date"]=input.Str("date"),["scope"]="These are current reports, not traffic predictions for your selected date. Unknown crowd levels do not mean a place is quiet."};
        async Task<JsonObject> Load(string kind){try{return await Feed(kind,token);}catch(Exception e)when(e is ServiceException or HttpRequestException or System.Text.Json.JsonException || e is OperationCanceledException&&!token.IsCancellationRequested){return new JsonObject{["unavailable"]=true};}}
        var trafficTask=Load("traffic");var feeTask=Load("prices");await Task.WhenAll(trafficTask,feeTask);
        result["traffic"]=Traffic(await trafficTask,points,started);var rawFees=await feeTask;
        result["pricePlaces"]=FeeMarkers(rawFees,points);
        var places=new JsonArray();var googleCalls=0;
        foreach(var p in points.OfType<JsonObject>()) {
            var price=Price(p,rawFees);var googleStatus="not-needed";
            if(price.Str("status")=="unknown") {
                // Google data stays on Google maps, never in a public snapshot or model-training table.
                if(input.Str("mapProvider")!="google")googleStatus="google-map-required";
                else if(!paid.Allowed)googleStatus="paid-services-disabled";
                else if(secrets.Read()["GOOGLE_SERVER_KEY"].Length==0)googleStatus="key-not-configured";
                else if(googleCalls>=5)googleStatus="request-budget-reached";
                else {
                    googleCalls++;
                    try {price=await GoogleFallback(p,token);googleStatus="queried";}
                    catch(Exception e)when(e is ServiceException or HttpRequestException or System.Text.Json.JsonException || e is OperationCanceledException&&!token.IsCancellationRequested){googleStatus="unavailable";}
                }
            }
            places.Add(new JsonObject{["id"]=p.Str("id"),["name"]=p.Str("name"),["lat"]=p["lat"]?.DeepClone(),["lng"]=p["lng"]?.DeepClone(),["price"]=price,["googleFallback"]=googleStatus,
                ["crowd"]=new JsonObject{["status"]="unknown",["summary"]="Reliable crowd and queue data is not available.",["people"]=null,["waitMinutes"]=null,["forecast"]=false,["reason"]="OSM does not provide live foot traffic, and the official Apple and Google Places APIs do not provide popular times."}});
        }
        result["places"]=places;result["priceDataFetchedAt"]=rawFees.Str("insightFetchedAt");result["priceFeedStatus"]=rawFees.Bool("unavailable")?"unavailable":"ok";result["partial"]=true;
        result["attribution"]="Live Traffic NSW / Transport for NSW · © OpenStreetMap contributors · ODbL";
        return result;
    }
    async Task<JsonObject> GoogleFallback(JsonObject point,CancellationToken token) {
        // Explicit, paid, Google-map-only supplement. Report its actual path; never claim this came from Zetaris.
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var id=point.Str("id");HttpRequestMessage request;
        if(id.StartsWith("google:",StringComparison.Ordinal)&&Regex.IsMatch(id[7..],"^[A-Za-z0-9_-]{1,190}$")) {
            request=new(HttpMethod.Get,"https://places.googleapis.com/v1/places/"+id[7..]);
            request.Headers.Add("X-Goog-FieldMask","id,priceLevel,priceRange,attributions,googleMapsUri");
        }else {
            request=new(HttpMethod.Post,"https://places.googleapis.com/v1/places:searchText");
            request.Content=JsonContent.Create(new JsonObject{["textQuery"]=point.Str("name"),["pageSize"]=5,["locationBias"]=new JsonObject{["circle"]=new JsonObject{["center"]=new JsonObject{["latitude"]=point.Number("lat"),["longitude"]=point.Number("lng")},["radius"]=100}}});
            request.Headers.Add("X-Goog-FieldMask","places.id,places.displayName,places.location,places.priceLevel,places.priceRange,places.attributions,places.googleMapsUri");
        }
        using(request){
            request.Headers.Add("X-Goog-Api-Key",secrets.Read()["GOOGLE_SERVER_KEY"]);
            using var response=await http.SendAsync(request,deadline.Token);response.EnsureSuccessStatusCode();
            var raw=await response.Content.ReadFromJsonAsync<JsonObject>(deadline.Token)??new();
            if(raw.Array("places") is JsonArray candidates){
                string Name(string x)=>new(x.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                raw=candidates.OfType<JsonObject>().FirstOrDefault(c=>Name(c.Object("displayName").Str("text"))==Name(point.Str("name"))&&Distance(point.Number("lat"),point.Number("lng"),c.Object("location").Number("latitude",double.NaN),c.Object("location").Number("longitude",double.NaN))<=50)??new();
            }
            var price=GooglePrice(raw);price["retrievalPath"]="google-direct-supplement";return price;
        }
    }
    static double Distance(double a,double b,double c,double d){var dy=(a-c)*111320;var dx=(b-d)*111320*Math.Cos(a*Math.PI/180);return Math.Sqrt(dx*dx+dy*dy);}
    public static JsonObject Traffic(JsonObject raw,JsonArray points,DateTimeOffset fetched) {
        var markers=new JsonArray();var features=raw.Array("features");var available=features is not null&&!raw.Bool("unavailable");
        foreach(var item in (features??new()).OfType<JsonObject>()) {
            var g=item.Object("geometry");var xy=g.Array("coordinates");var p=item.Object("properties");if(g.Str("type")!="Point"||xy is null||xy.Count<2||p is null||p.Bool("ended"))continue;
            var lng=xy![0]!.GetValue<double>();var lat=xy[1]!.GetValue<double>();if(!OsmPoiProvider.ValidPoint(lat,lng))continue;
            if(!points.OfType<JsonObject>().Any(s=>Distance(s.Number("lat"),s.Number("lng"),lat,lng)<=2500))continue;
            var title=p.Str("headline");if(string.IsNullOrWhiteSpace(title))title=p.Str("mainCategory","Road incident report");
            markers.Add(new JsonObject{["id"]=item["id"]?.ToString()??"",["lat"]=lat,["lng"]=lng,["title"]=title,["summary"]=p.Str("adviceA"),["updatedAt"]=p["lastUpdated"]?.DeepClone(),["source"]="Live Traffic NSW"});
        }
        return new JsonObject{["status"]=available?"reported":"unavailable",["items"]=markers,["source"]="Live Traffic NSW",["summary"]=available?$"{markers.Count} nearby road reports · not live traffic speeds":"Road reports are temporarily unavailable.",["fetchedAt"]=raw.Str("insightFetchedAt",fetched.ToString("O")),["appliesTo"]="now",["speedAvailable"]=false,["etaAvailable"]=false};
    }
    public static JsonArray FeeMarkers(JsonObject raw,JsonArray points){
        var items=new JsonArray();
        foreach(var e in (raw.Array("elements")??new()).OfType<JsonObject>()){
            var center=e.Object("center")??e;var tags=e.Object("tags");var lat=center.Number("lat",double.NaN);var lng=center.Number("lon",double.NaN);
            if(!OsmPoiProvider.ValidPoint(lat,lng)||tags is null||tags.Str("name").Length==0||tags.Str("charge").Length==0)continue;
            if(tags.Str("tourism").Length==0&&tags.Str("leisure").Length==0&&tags.Str("historic").Length==0&&tags.Str("amenity") is not ("restaurant" or "cafe" or "fast_food" or "food_court" or "bar" or "pub"))continue;
            if(!points.OfType<JsonObject>().Any(p=>Distance(p.Number("lat"),p.Number("lng"),lat,lng)<=2500))continue;
            items.Add(new JsonObject{["id"]="osm:"+e.Str("type")+":"+e["id"],["lat"]=lat,["lng"]=lng,["title"]=tags.Str("name"),["summary"]="Reference charge: "+tags.Str("charge")+" · not a live quote",["source"]="OpenStreetMap",["kind"]="price"});
        }
        return items;
    }
    public static JsonObject Price(JsonObject point,JsonObject raw) {
        foreach(var e in (raw.Array("elements")??new()).OfType<JsonObject>()) {
            var center=e.Object("center")??e;var tags=e.Object("tags");if(tags is null||tags.Str("name").Trim().ToLowerInvariant()!=point.Str("name").Trim().ToLowerInvariant())continue;
            if(Distance(point.Number("lat"),point.Number("lng"),center.Number("lat",double.NaN),center.Number("lon",double.NaN)) is not <=50)continue;
            var charge=tags.Str("charge");if(charge.Length==0)continue;
            return new JsonObject{["status"]="reported",["summary"]="Reference charge: "+charge,["amount"]=null,["currency"]=null,["source"]="OpenStreetMap",["sourceUrl"]="https://www.openstreetmap.org/"+e.Str("type")+"/"+e["id"],["scope"]="OSM fee tags may not represent a per-person meal cost or today's admission price. Not automatically added to your budget."};
        }
        return new JsonObject{["status"]="unknown",["summary"]="Cost unknown · not added to your budget",["amount"]=null,["currency"]=null,["source"]=raw.Bool("unavailable")?"The open fee data source is temporarily unavailable.":"OpenStreetMap has no matching fee information."};
    }
    public static JsonObject GooglePrice(JsonObject raw) {
        var range=raw.Object("priceRange");var level=raw.Str("priceLevel");
        return new JsonObject{["status"]=range is not null||level.Length>0?"reported":"unknown",["summary"]=range is not null?"Google reference price range (details below)":level.Length>0?"Google price level: "+level:"Google has not provided a price.",["range"]=range?.DeepClone(),["level"]=level,["amount"]=null,["source"]="Google Maps",["attributions"]=raw.Array("attributions")?.DeepClone(),["mapsURL"]=raw.Str("googleMapsUri"),["scope"]="Reference price, not a quote for your visit. Not automatically added to your budget."};
    }
}
