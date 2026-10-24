using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
namespace SydneyBuddy.Api;

/// <summary>Nearby Commons photography, explicitly not verified business/interior photos.</summary>
public sealed class PoiMediaService(HttpClient http,IMemoryCache cache) {
    public async Task<JsonObject> LoadAsync(JsonObject input,CancellationToken token) {
        var lat=input.Number("lat",double.NaN);var lng=input.Number("lng",double.NaN);
        if(!OsmPoiProvider.ValidPoint(lat,lng))throw new ServiceException("地點座標無效。");
        var point=FormattableString.Invariant($"{lat:F6}|{lng:F6}");var key="poi-media:"+point;
        if(cache.TryGetValue<JsonObject>(key,out var saved))return saved!.DeepClone().AsObject();
        var url="https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=geosearch&ggsprimary=all&ggsnamespace=6&ggsradius=100&ggslimit=8&ggscoord="+Uri.EscapeDataString(point)+"&prop=imageinfo&iiprop=url%7Cextmetadata%7Cmime&iiurlwidth=640";
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.UserAgent.ParseAdd("SydneyBuddy/1.0 (https://github.com/f1sh11/hackathon)");
        using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
        var data=JsonNode.Parse(await response.Content.ReadAsStringAsync(token))?.AsObject();
        if(data?.ContainsKey("error")==true)throw new ServiceException("照片服務暫時無法載入，請重試。");
        var photos=new JsonArray();
        foreach(var page in (data?["query"]?["pages"] as JsonObject ?? new JsonObject()).Select(p=>p.Value).OfType<JsonObject>()) {
            var info=page.Array("imageinfo")?.OfType<JsonObject>().FirstOrDefault();if(info is null)continue;
            var image=info.Str("thumburl");var pageUrl=info.Str("descriptionurl");
            if(info.Str("mime") is not ("image/jpeg" or "image/png" or "image/webp") || !SafeUrl(image,"upload.wikimedia.org") || !SafeUrl(pageUrl,"commons.wikimedia.org"))continue;
            var meta=info["extmetadata"] as JsonObject;
            string Value(string name)=>Plain(meta?[name]?["value"]?.GetValue<string>()??"");
            var license=Value("LicenseShortName");if(license.Length==0)continue;
            photos.Add(new JsonObject{["url"]=image,["pageUrl"]=pageUrl,["title"]=Plain(page.Str("title").Replace("File:","")),["artist"]=Value("Artist"),["license"]=license});
        }
        var result=new JsonObject{["photos"]=photos,["source"]="Wikimedia Commons",["nearbyRadiusMeters"]=100};
        cache.Set(key,result.DeepClone().AsObject(),TimeSpan.FromHours(6));return result;
    }
    static bool SafeUrl(string raw,string host)=>Uri.TryCreate(raw,UriKind.Absolute,out var url)&&url.Scheme=="https"&&url.Host==host;
    static string Plain(string raw)=>WebUtility.HtmlDecode(Regex.Replace(raw,"<[^>]*>","",RegexOptions.None,TimeSpan.FromMilliseconds(100))).Trim();
}
