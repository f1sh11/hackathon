using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
namespace SydneyBuddy.Api;

/// <summary>Targeted and nearby Commons photography with relevance scoring and license attribution.</summary>
public sealed class PoiMediaService(HttpClient http,IMemoryCache cache,HiddenGemService? gems=null) {
    static readonly Dictionary<string,string> KnownEnglishNames=new(StringComparer.OrdinalIgnoreCase){
        ["悉尼歌剧院"]="Sydney Opera House",["雪梨歌劇院"]="Sydney Opera House",
        ["岩石区"]="The Rocks Sydney",["岩石區"]="The Rocks Sydney",
        ["皇家植物园"]="Royal Botanic Garden Sydney",["皇家植物園"]="Royal Botanic Garden Sydney",
        ["澳大利亚当代艺术博物馆"]="Museum of Contemporary Art Australia",["澳洲當代藝術博物館"]="Museum of Contemporary Art Australia",
        ["维多利亚女王大厦"]="Queen Victoria Building",["維多利亞女王大廈"]="Queen Victoria Building",
        ["新南威尔士州美术馆"]="Art Gallery of New South Wales",["新南威爾斯美術館"]="Art Gallery of New South Wales",
        ["澳大利亚博物馆"]="Australian Museum Sydney",["澳洲博物館"]="Australian Museum Sydney",
        ["达令港"]="Darling Harbour",["達令港"]="Darling Harbour",
        ["麥格理夫人椅"]="Mrs Macquarie's Chair",["麦格理夫人椅"]="Mrs Macquarie's Chair",
        ["卡希爾高速公路觀景台"]="Cahill Expressway Lookout",["卡希尔高速公路观景台"]="Cahill Expressway Lookout",
        ["海德公園"]="Hyde Park Sydney",["海德公园"]="Hyde Park Sydney",
        ["聖瑪麗大教堂"]="St Mary's Cathedral Sydney",["圣玛丽大教堂"]="St Mary's Cathedral Sydney",
        ["雪梨海港大橋"]="Sydney Harbour Bridge",["悉尼海港大桥"]="Sydney Harbour Bridge",
        ["塔龍加動物園"]="Taronga Zoo Sydney",["塔龙加动物园"]="Taronga Zoo Sydney",
        ["邦迪海灘"]="Bondi Beach",["邦迪海滩"]="Bondi Beach",
        ["曼利海灘"]="Manly Beach",["曼利海滩"]="Manly Beach",
        ["Lindesay"]="Lindesay Darling Point",
        ["Cremorne Reserve"]="Robertson Point Light",
        ["Sawmillers Reserve"]="Berrys Bay",
        ["Trim the Cat"]="Trim the cat"
    };
    static readonly string[] NoiseKeywords={
        "sticker art","graffiti","1906 reserva","ford xm","eda utku","alan atkinson","no planet b","polar bear",
        "catfish","brooklyn, new south wales","donington, lincs","morocco","covid-19","circa 18","1900-1914",
        "dec 1923","1924","ca 1930","ca. 1920","six men standing","four men outside","acting as a flagship",
        "coat of arms","map of","floor plan","diagram","logo","timetable","menu","receipt","qr code"
    };
    static readonly HashSet<string> StopWords=new(StringComparer.OrdinalIgnoreCase){
        "sydney","nsw","australia","the","and","for","from","with","near","street","road","lane","place","point","park","reserve","museum","gallery","hotel","cafe","restaurant","bar","shop"
    };

    public async Task<JsonObject> LoadAsync(JsonObject input,CancellationToken token) {
        if(input.Str("id").StartsWith("gem:")){
            var gemMedia=gems?.Media(input.Str("id"))??throw new ServiceException("The hidden gem source is not configured.");
            if(gemMedia.Array("photos")?.Count>0||input.Str("name").Length==0)return gemMedia;
        }
        var lat=input.Number("lat",double.NaN);var lng=input.Number("lng",double.NaN);
        if(!OsmPoiProvider.ValidPoint(lat,lng))throw new ServiceException("Place coordinates are invalid.");
        var rawName=input.Str("name").Trim();
        var point=FormattableString.Invariant($"{lat:F6}|{lng:F6}");
        var key=rawName.Length==0?"poi-media:"+point:"poi-media:"+point+":"+rawName.ToLowerInvariant();
        if(cache.TryGetValue<JsonObject>(key,out var saved))return saved!.DeepClone().AsObject();

        // Preserve exact single-call geosearch contract when no place name is provided.
        if(rawName.Length==0){
            var geoUrl="https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=geosearch&ggsprimary=all&ggsnamespace=6&ggsradius=100&ggslimit=8&ggscoord="+Uri.EscapeDataString(point)+"&prop=imageinfo&iiprop=url%7Cextmetadata%7Cmime&iiurlwidth=640";
            var geoPhotos=await FetchCommonsAsync(geoUrl,null,false,token);
            var geoResult=new JsonObject{["photos"]=geoPhotos,["source"]="Wikimedia Commons",["nearbyRadiusMeters"]=100};
            cache.Set(key,geoResult.DeepClone().AsObject(),TimeSpan.FromHours(6));return geoResult;
        }

        var searchName=CanonicalSearchName(rawName);
        var photos=new JsonArray();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        void AppendUnique(JsonArray source,int maxTotal=6){
            foreach(var item in source.OfType<JsonObject>()){
                if(photos.Count>=maxTotal)break;
                if(seen.Add(item.Str("pageUrl")))photos.Add(item.DeepClone());
            }
        }

        if(searchName.Length>=3){
            var hasSydney=searchName.Contains("Sydney",StringComparison.OrdinalIgnoreCase);
            var q1=hasSydney?$"intitle:\"{searchName}\" filetype:bitmap":$"intitle:\"{searchName}\" Sydney filetype:bitmap";
            var url1="https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=10&gsrsearch="+Uri.EscapeDataString(q1)+"&prop=imageinfo&iiprop=url%7Cextmetadata%7Cmime&iiurlwidth=640";
            AppendUnique(await FetchCommonsAsync(url1,searchName,true,token));

            if(photos.Count<3){
                var q2=hasSydney?$"\"{searchName}\" filetype:bitmap":$"\"{searchName}\" Sydney filetype:bitmap";
                var url2="https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=10&gsrsearch="+Uri.EscapeDataString(q2)+"&prop=imageinfo&iiprop=url%7Cextmetadata%7Cmime&iiurlwidth=640";
                AppendUnique(await FetchCommonsAsync(url2,searchName,true,token));
            }
        }

        if(photos.Count<3){
            var fallbackUrl="https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=geosearch&ggsprimary=all&ggsnamespace=6&ggsradius=150&ggslimit=12&ggscoord="+Uri.EscapeDataString(point)+"&prop=imageinfo&iiprop=url%7Cextmetadata%7Cmime&iiurlwidth=640";
            AppendUnique(await FetchCommonsAsync(fallbackUrl,searchName,false,token));
        }

        var result=new JsonObject{["photos"]=photos,["source"]="Wikimedia Commons",["nearbyRadiusMeters"]=100};
        cache.Set(key,result.DeepClone().AsObject(),TimeSpan.FromHours(6));return result;
    }

    internal static string CanonicalSearchName(string rawName){
        var trimmed=rawName.Split('·')[0].Trim();
        if(KnownEnglishNames.TryGetValue(trimmed,out var mapped))return mapped;
        if(KnownEnglishNames.TryGetValue(rawName,out mapped))return mapped;
        return trimmed.Replace("&","and").Trim();
    }

    internal static int ScoreTitle(string title,string? searchName){
        if(string.IsNullOrWhiteSpace(searchName))return 1;
        var lowerTitle=title.ToLowerInvariant();
        var lowerName=searchName.ToLowerInvariant();
        if(lowerTitle.Contains(lowerName))return 100;
        var tokens=Regex.Matches(lowerName,@"[a-z0-9]{3,}").Select(m=>m.Value).Where(t=>!StopWords.Contains(t)).Distinct().ToArray();
        if(tokens.Length==0){
            tokens=Regex.Matches(lowerName,@"[a-z0-9]{3,}").Select(m=>m.Value).Where(t=>t!="sydney"&&t!="nsw"&&t!="the").Distinct().ToArray();
        }
        var matched=tokens.Count(t=>lowerTitle.Contains(t));
        return matched*30;
    }

    internal static bool IsNoiseTitle(string title){
        var lower=title.ToLowerInvariant();
        if(NoiseKeywords.Any(lower.Contains))return true;
        if(Regex.IsMatch(lower,@"nsw\s+20\d\d,\s*australia\s*-\s*panoramio"))return true;
        return false;
    }

    async Task<JsonArray> FetchCommonsAsync(string url,string? searchName,bool requireNameMatch,CancellationToken token){
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.UserAgent.ParseAdd("SydneyBuddy/1.0 (https://github.com/f1sh11/hackathon)");
        using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
        var data=JsonNode.Parse(await response.Content.ReadAsStringAsync(token))?.AsObject();
        if(data?.ContainsKey("error")==true)throw new ServiceException("Photos are temporarily unavailable. Please try again.");
        var scored=new List<(int Score,JsonObject Photo)>();
        foreach(var page in (data?["query"]?["pages"] as JsonObject ?? new JsonObject()).Select(p=>p.Value).OfType<JsonObject>()) {
            var info=page.Array("imageinfo")?.OfType<JsonObject>().FirstOrDefault();if(info is null)continue;
            var image=info.Str("thumburl");var pageUrl=info.Str("descriptionurl");
            if(info.Str("mime") is not ("image/jpeg" or "image/png" or "image/webp") || !(SafeUrl(image,"upload.wikimedia.org")||SafeUrl(image,"thumb.wikimedia.org")) || !SafeUrl(pageUrl,"commons.wikimedia.org"))continue;
            var meta=info["extmetadata"] as JsonObject;
            string Value(string name)=>Plain(meta?[name]?["value"]?.GetValue<string>()??"");
            var license=Value("LicenseShortName");if(license.Length==0)continue;
            var title=Plain(page.Str("title").Replace("File:",""));
            if(searchName is not null && IsNoiseTitle(title))continue;
            var score=ScoreTitle(title,searchName);
            if(requireNameMatch && score<=0)continue;
            var matchKind=searchName is null ? null : (score>0 ? "exact-place" : "nearby-unverified");
            var obj=new JsonObject{["url"]=image,["pageUrl"]=pageUrl,["title"]=title,["artist"]=Value("Artist"),["license"]=license};
            if(matchKind is not null)obj["matchKind"]=matchKind;
            scored.Add((score,obj));
        }
        var photos=new JsonArray();
        foreach(var item in (searchName is null ? scored.AsEnumerable() : scored.OrderByDescending(x=>x.Score).ThenBy(x=>x.Photo.Str("title"))))photos.Add(item.Photo);
        return photos;
    }

    static bool SafeUrl(string raw,string host)=>Uri.TryCreate(raw,UriKind.Absolute,out var url)&&url.Scheme=="https"&&url.Host==host;
    static string Plain(string raw)=>WebUtility.HtmlDecode(Regex.Replace(raw,"<[^>]*>","",RegexOptions.None,TimeSpan.FromMilliseconds(100))).Trim();
}
