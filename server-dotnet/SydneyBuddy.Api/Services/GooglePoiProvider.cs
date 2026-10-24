using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>Google results stay in the current search view; never use the OSM cache or demo editor.</summary>
public sealed class GooglePoiProvider(IExternalJsonClient external,SecretStore secrets,PaidServicePolicy policy) {
    public async Task<JsonObject> SearchResultAsync(double lat,double lng,int radius,string category,string text,string language,CancellationToken token) {
        policy.RequireAllowed();
        var key=secrets.Read().GetValueOrDefault("GOOGLE_SERVER_KEY");
        if(string.IsNullOrWhiteSpace(key))throw new ServiceException("Google Places 尚未設定伺服器金鑰，請在本機設定頁配置。");
        if(language is not("zh-TW" or "zh-CN" or "en"))language="en";
        var types=category switch {"food"=>new[]{"restaurant","cafe","bakery"},"facilities"=>new[]{"public_bathroom"},_=>new[]{"tourist_attraction","museum","park","art_gallery"}};
        var circle=new JsonObject{["center"]=new JsonObject{["latitude"]=lat,["longitude"]=lng},["radius"]=radius};
        var payload=new JsonObject{["maxResultCount"]=20,["languageCode"]=language};
        var nearby=text.Length==0;
        if(nearby){payload["includedTypes"]=new JsonArray(types.Select(t=>(JsonNode?)JsonValue.Create(t)).ToArray());payload["locationRestriction"]=new JsonObject{["circle"]=circle};payload["rankPreference"]="DISTANCE";}
        else{payload["textQuery"]=text;payload["locationBias"]=new JsonObject{["circle"]=circle};}
        var raw=await external.FetchAsync("https://places.googleapis.com/v1/places:"+(nearby?"searchNearby":"searchText"),payload,new Dictionary<string,string>{["X-Goog-Api-Key"]=key,["X-Goog-FieldMask"]="places.id,places.displayName,places.location,places.formattedAddress,places.primaryType,places.types,places.googleMapsUri,places.attributions"},cancellationToken:token);
        var places=new JsonArray();var attributions=new HashSet<string>();
        foreach(var p in (raw.Array("places")??new()).OfType<JsonObject>()) {
            var returnedTypes=(p.Array("types")??new()).Select(x=>x?.ToString()??"").Append(p.Str("primaryType"));
            if(!returnedTypes.Any(types.Contains))continue;
            var point=p.Object("location");var a=point.Number("latitude",double.NaN);var b=point.Number("longitude",double.NaN);
            if(!OsmPoiProvider.ValidPoint(a,b)||p.Str("id").Length==0||p.Object("displayName").Str("text").Length==0)continue;
            // Text Search's locationBias is advisory: enforce the requested nearby area ourselves.
            var dy=(a-lat)*111320;var dx=(b-lng)*111320*Math.Cos(lat*Math.PI/180);
            if(Math.Sqrt(dx*dx+dy*dy)>radius)continue;
            foreach(var author in (p.Array("attributions")??new()).OfType<JsonObject>())if(author.Str("provider").Length>0)attributions.Add(author.Str("provider"));
            places.Add(new JsonObject{["id"]="google:"+p.Str("id"),["name"]=p.Object("displayName").Str("text"),["lat"]=a,["lng"]=b,["source"]="Google Places",["category"]=category=="food"?"美食":category=="facilities"?"設施":"風景",["poiKind"]=p.Str("primaryType"),["indoor"]=false,["visitMinutes"]=category=="food"?45:category=="facilities"?10:60,["cost"]=0,["description"]=p.Str("formattedAddress","未提供地址"),["note"]="Google Places 搜尋結果；費用、排隊與營業狀態未確認。",["reason"]="Google 地點搜尋",["mapsURL"]=p.Str("googleMapsUri"),["openingHoursText"]=""});
        }
        return new JsonObject{["places"]=places,["source"]="Google Places",["attribution"]="Google Maps"+(attributions.Count>0?" · "+string.Join(" · ",attributions):""),["message"]="Google 地點僅用於目前 Google 地圖搜尋；本機示例時間軸暫不匯入此來源。"};
    }
}
