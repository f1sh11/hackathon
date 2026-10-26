using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
namespace SydneyBuddy.Api;

public sealed class TravelDataContext(IHttpContextAccessor accessor,IConfiguration? config=null) {
    public const string Header="X-Travel-Data-Source";
    public string? ManagedMode {
        get {var value=config?["TravelData:ManagedSource"];return string.IsNullOrWhiteSpace(value)?null:value is "direct" or "zetaris" or "zetaris-cloud"?value:throw new ServiceException("後端旅遊資料來源設定無效。");}
    }
    public bool Explicit=>ManagedMode is not null||accessor.HttpContext?.Request.Headers.ContainsKey(Header)==true;
    public string Mode {
        get {var mode=ManagedMode??accessor.HttpContext?.Request.Headers[Header].ToString()??"";return mode is "" or "direct"?"direct":mode is "zetaris" or "zetaris-cloud"?mode:throw new ServiceException("不支援的旅遊資料來源。");}
    }
    public bool Warehouse=>Mode is "zetaris" or "zetaris-cloud";
    public TravelSnapshot? Snapshot=>accessor.HttpContext?.Items[typeof(TravelSnapshot)] as TravelSnapshot;
}
public sealed record TravelSnapshot(JsonObject Poi,JsonArray Catalog,JsonArray Maps,string ReadAt,JsonArray? Gems=null,JsonArray? Osm=null,JsonArray? Social=null,JsonArray? Restaurants=null);
public interface ITravelWarehouse {
    Task<TravelSnapshot> ReadAsync(CancellationToken token);
    Task<JsonArray> ActivityOffersAsync(CancellationToken token)=>throw new ServiceException("Zetaris 活動資料尚未設定。");
    Task<JsonObject> InsightRawAsync(string kind,CancellationToken token)=>throw new ServiceException("Zetaris 尚未設定此公開資料來源。");
    Task<JsonObject> WeatherRawAsync(CancellationToken token)=>throw new ServiceException("Zetaris 即時天氣尚未設定。");
}

/// <summary>Only fixed public SELECTs leave this backend. Weather uses an uncached native Zetaris REST table.</summary>
public sealed class ZetarisTravelWarehouse(HttpClient http,IConfiguration config,IMemoryCache cache,TravelDataContext? selection=null):ITravelWarehouse {
    static readonly SemaphoreSlim Gate=new(1,1);
    bool Cloud=>selection?.Mode=="zetaris-cloud";
    string? Setting(string key)=>config["Zetaris:"+(Cloud?"Cloud:":"Local:")+key] ?? (Cloud?null:config["Zetaris:"+key]);
    Uri Endpoint(string key,string localDefault){
        var raw=Setting(key)??(Cloud?"":localDefault);
        if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||
            (Cloud ? uri.Scheme!="https"||uri.IsLoopback : uri.Scheme!="http"||!uri.IsLoopback))
            throw new ServiceException(Cloud?"雲端 Zetaris 尚未設定有效的 HTTPS 連線。":"Zetaris 本機連線設定無效。");
        return uri;
    }
    async Task<JsonArray> QueryAsync(string sql,CancellationToken token){
        try {
            var api=Endpoint("BaseUrl","http://127.0.0.1:8888");
            var proxy=Setting("Transport")=="ui-proxy";
            var bearer=Setting("ApiKey")??"";var org=Setting("OrgId");
            if(bearer.Length==0){
                var login=Endpoint("LoginUrl","http://127.0.0.1:3000/api/auth/login");
            var env=new Dictionary<string,string>();var file=Setting("EnvironmentFile");
            // A cloud request never reads the local platform's admin credentials.
            if(!Cloud&&!string.IsNullOrEmpty(file)&&File.Exists(file))foreach(var line in File.ReadLines(file)){
                if(line.StartsWith('#'))continue;var at=line.IndexOf('=');
                if(at>0)env[line[..at].Trim()]=line[(at+1)..].Trim().Trim('"','\'');
            }
            var username=Setting("Username")??env.GetValueOrDefault("ZETARIS_ADMIN_EMAIL");
            var password=Setting("Password")??env.GetValueOrDefault("ZETARIS_ADMIN_PASSWORD");
            if(string.IsNullOrEmpty(username)||string.IsNullOrEmpty(password))throw new ServiceException("Zetaris 尚未設定後端連線。可切回直接來源。");
            using var authResponse=await http.PostAsJsonAsync(login,new{username,password},token);
            if(!authResponse.IsSuccessStatusCode)throw new ServiceException("Zetaris 登入失敗，請檢查後端設定。");
            var auth=JsonNode.Parse(await authResponse.Content.ReadAsStringAsync(token))?.AsObject()??throw new ServiceException("Zetaris 登入回應無效。");
            bearer=auth.Str("idToken");org=auth["orgId"]?.ToString();
            if(Setting("OrgId") is {Length:>0} expectedOrg && org!=expectedOrg)throw new ServiceException("Zetaris 登入組織與設定不符。");
            }
            if(bearer.Length==0||string.IsNullOrEmpty(org))throw new ServiceException("Zetaris 登入或應用程式組織設定無效。");
            using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(api,proxy?"/api/proxy/sql-editor/sqls/run-query":"/api/v1.0/sql-editor/sqls/run"));
            request.Headers.Authorization=new("Bearer",bearer);request.Headers.Add("X-Org-ID",org);request.Headers.Add("X-Request-ID",Guid.NewGuid().ToString());
            request.Headers.CacheControl=new(){NoCache=true,NoStore=true};
            request.Content=proxy ? JsonContent.Create(new{sql,limit=20000}) : JsonContent.Create(new{queryId=Guid.NewGuid(),sql,source="SqlEditor",limit=20000});
            using var response=await http.SendAsync(request,token);
            if(!response.IsSuccessStatusCode)throw new ServiceException("Zetaris 公開資料查詢失敗，未改用其他來源。");
            var raw=JsonNode.Parse(await response.Content.ReadAsStringAsync(token))?.AsObject()??throw new ServiceException("Zetaris 回應無效。");
            var rows=raw.Array("data")??throw new ServiceException("Zetaris 未返回資料。");
            if(rows.Count==0||rows.Any(r=>r is not JsonArray)||rows.Count>=20000||raw.Int("total",rows.Count)!=rows.Count)
                throw new ServiceException("Zetaris 快照不完整或超過本機測試上限。");
            return rows;
        }catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new ServiceException("Zetaris 連線逾時，可切回直接來源。");}
        catch(HttpRequestException){throw new ServiceException("無法連接 Zetaris，可切回直接來源。");}
        catch(System.Text.Json.JsonException){throw new ServiceException("Zetaris 回應格式無效。");}
    }
    public async Task<JsonArray> ActivityOffersAsync(CancellationToken token){
        // Sentinel preserves an explicitly empty collection; no fallback and no snapshot cache.
        var rows=await QueryAsync("SELECT 'offer' AS kind,payload FROM sydneybuddy_travel.travel_activity_offers UNION ALL SELECT 'sentinel','{}'",token);
        var result=new JsonArray();
        foreach(var row in rows.OfType<JsonArray>()){
            if(row.Count!=2)throw new ServiceException("Zetaris 活動資料格式無效。");
            if(row[0]!.GetValue<string>()=="sentinel")continue;
            result.Add(JsonNode.Parse(row[1]!.GetValue<string>())?.AsObject()??throw new ServiceException("Zetaris 活動資料格式無效。"));
        }
        return result;
    }
    public async Task<JsonObject> WeatherRawAsync(CancellationToken token){
        // Never read travel_weather, snapshot cache, or CACHE TABLE here.
        var rows=await QueryAsync("SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live.weather_sydney",token);
        if(rows.Count!=1||rows[0] is not JsonArray row||row.Count!=1)throw new ServiceException("Zetaris 即時天氣格式無效。");
        return JsonNode.Parse(row[0]!.GetValue<string>())?.AsObject()??throw new ServiceException("Zetaris 即時天氣資料無效。");
    }
    public async Task<JsonObject> InsightRawAsync(string kind,CancellationToken token){
        var table=kind switch {"traffic"=>"traffic_nsw","prices"=>"fees_sydney",_=>throw new ServiceException("未知資料種類。")};
        var rows=await QueryAsync("SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live."+table,token);
        if(rows.Count!=1||rows[0] is not JsonArray row||row.Count!=1)throw new ServiceException("Zetaris 公開資料格式無效。");
        return JsonNode.Parse(row[0]!.GetValue<string>())?.AsObject()??throw new ServiceException("Zetaris 公開資料格式無效。");
    }
    public async Task<TravelSnapshot> ReadAsync(CancellationToken token){
        var key="travel-public-v6:"+(Cloud?"cloud":"local")+":"+Setting("RestaurantsEnabled")+":"+Setting("SocialEnabled")+":"+Setting("BaseUrl")+":"+Setting("LoginUrl")+":"+Setting("Transport")+":"+Setting("Username")+":"+Setting("OrgId")+":"+Setting("EnvironmentFile");
        if(cache.TryGetValue<TravelSnapshot>(key,out var hit))return hit!;
        await Gate.WaitAsync(token);
        try {
            if(cache.TryGetValue<TravelSnapshot>(key,out hit))return hit!;
            var sql="SELECT 'poi' AS dataset,payload FROM sydneybuddy_travel.travel_poi UNION ALL SELECT 'catalog',payload FROM sydneybuddy_travel.travel_catalog UNION ALL SELECT 'maps',payload FROM sydneybuddy_travel.travel_map_layers UNION ALL SELECT 'metadata',payload FROM sydneybuddy_travel.travel_metadata UNION ALL SELECT 'gems',payload FROM sydneybuddy_travel.travel_hidden_gems UNION ALL SELECT 'osm',payload FROM sydneybuddy_travel.travel_osm_places";
            if(bool.TryParse(Setting("SocialEnabled"),out var socialEnabled)&&socialEnabled)sql+=" UNION ALL SELECT 'social',payload FROM sydneybuddy_travel.travel_social_places";
            if(bool.TryParse(Setting("RestaurantsEnabled"),out var restaurantsEnabled)&&restaurantsEnabled)sql+=" UNION ALL SELECT 'restaurants',payload FROM sydneybuddy_travel.travel_restaurant_places";
            var rows=await QueryAsync(sql,token);
            var groups=new Dictionary<string,JsonArray>{{"poi",new()},{"catalog",new()},{"maps",new()},{"metadata",new()},{"gems",new()},{"osm",new()},{"social",new()},{"restaurants",new()}};
            foreach(var row in rows.OfType<JsonArray>()){
                if(row.Count!=2||!groups.TryGetValue(row[0]!.GetValue<string>(),out var group))throw new ServiceException("Zetaris 資料格式不一致。");
                group.Add(JsonNode.Parse(row[1]!.GetValue<string>())?.AsObject()??throw new ServiceException("Zetaris 地點資料無效。"));
            }
            if(groups["poi"].Count==0||groups["catalog"].Count==0||groups["maps"].Count==0||groups["metadata"].Count!=1)throw new ServiceException("Zetaris 缺少必要的公開資料表。");
            var poi=groups["metadata"][0]!.DeepClone().AsObject();poi["places"]=groups["poi"];
            if(poi.Str("source")!="Overture Maps"||poi.Array("bbox")?.Count!=4)throw new ServiceException("Zetaris 地點來源或涵蓋範圍無效。");
            var value=new TravelSnapshot(poi,groups["catalog"],groups["maps"],DateTimeOffset.UtcNow.ToString("O"),groups["gems"],groups["osm"],groups["social"],groups["restaurants"]);
            cache.Set(key,value,TimeSpan.FromSeconds(15));return value;
        }finally{Gate.Release();}
    }
}
public sealed class TravelDataMiddleware(RequestDelegate next) {
    static readonly string[] Paths=["/poi/search","/poi/media","/catalog","/schedule/editor","/plan","/data/status","/data/map-layers","/pet"];
    public async Task InvokeAsync(HttpContext context,TravelDataContext selection,ITravelWarehouse warehouse){
        var mode=selection.Mode;
        if(selection.Warehouse&&((context.Request.Path=="/restaurants/import")||(context.Request.Path.StartsWithSegments("/social")&&context.Request.Path.Value!.EndsWith("/publish",StringComparison.Ordinal))||Paths.Any(path=>context.Request.Path.StartsWithSegments(path)) || (HttpMethods.IsPost(context.Request.Method)&&context.Request.Path=="/trips")))context.Items[typeof(TravelSnapshot)]=await warehouse.ReadAsync(context.RequestAborted);
        context.Response.Headers[TravelDataContext.Header]=mode;await next(context);
    }
}

public sealed class PublicTravelWeather(HttpClient http,TravelDataContext selection,ITravelWarehouse warehouse) {
    public async Task<JsonObject> GetAsync(JsonObject profile,CancellationToken token){
        JsonObject raw;
        if(selection.Warehouse)raw=await warehouse.WeatherRawAsync(token);
        else {
            try {
                using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.open-meteo.com/v1/forecast?latitude=-33.8688&longitude=151.2093&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Australia%2FSydney&forecast_days=7");
                request.Headers.CacheControl=new(){NoCache=true,NoStore=true};
                using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();
                raw=await response.Content.ReadFromJsonAsync<JsonObject>(token)??throw new ServiceException("天氣回應無效。");
            }catch(HttpRequestException){throw new ServiceException("無法連接 Open-Meteo 天氣來源。");}
            catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new ServiceException("天氣查詢逾時。");}
        }
        var now=DateTimeOffset.UtcNow;
        var result=Forecast(Normalize(raw,now),profile,selection.Mode,now);
        result["live"]=true;result["cached"]=false;result["providerGenerationMs"]=raw["generationtime_ms"]?.DeepClone();
        return result;
    }
    static double? Number(JsonNode? n)=>n is JsonValue v&&v.TryGetValue<double>(out var d)&&double.IsFinite(d)?d:null;
    public static JsonArray Normalize(JsonObject raw,DateTimeOffset fetched){
        if(raw.Str("timezone")!="Australia/Sydney")throw new ServiceException("天氣時區無效。");
        var daily=raw.Object("daily")??throw new ServiceException("天氣回應缺少每日資料。");var dates=daily.Array("time")??new();var result=new JsonArray();
        for(var i=0;i<dates.Count;i++){
            double? Read(string key){var values=daily.Array(key);return values is not null&&i<values.Count?Number(values[i]):null;}
            var low=Read("temperature_2m_min");var high=Read("temperature_2m_max");var probability=Read("precipitation_probability_max");var code=Read("weather_code");
            var available=low is not null&&high is not null&&low<=high&&probability is >=0 and <=100&&code is not null;
            result.Add(new JsonObject{["date"]=dates[i]!.GetValue<string>(),["available"]=available,["temperatureMin"]=low,["temperatureMax"]=high,["precipitationProbability"]=probability,["rainy"]=available&&(probability>=60||code is 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 or 95 or 96 or 99),["fetchedAt"]=fetched.ToString("O"),["source"]="Open-Meteo",["timezone"]="Australia/Sydney"});
        }
        return result;
    }
    public static JsonObject Forecast(JsonArray records,JsonObject profile,string mode,DateTimeOffset now){
        var start=DateOnly.ParseExact(profile.Str("date"),"yyyy-MM-dd",CultureInfo.InvariantCulture);var days=new JsonArray();var anyRain=false;var available=0;
        for(var i=0;i<profile.Int("days",1);i++){
            var date=start.AddDays(i).ToString("yyyy-MM-dd");var row=records.OfType<JsonObject>().FirstOrDefault(r=>r.Str("date")==date);
            var fresh=DateTimeOffset.TryParse(row?.Str("fetchedAt"),out var fetched)&&now-fetched<=TimeSpan.FromHours(6)&&fetched<=now.AddMinutes(5);
            var ok=row?.Bool("available")==true&&fresh;var rainy=ok&&row!.Bool("rainy");anyRain|=rainy;if(ok)available++;
            var item=row?.DeepClone().AsObject()??new JsonObject();item["date"]=date;item["available"]=ok;item["rainy"]=rainy;item["stale"]=row is not null&&!fresh;
            item["summary"]=ok?$"{row!.Number("temperatureMin"):0.#}–{row.Number("temperatureMax"):0.#}°C · 降雨機率 {row.Number("precipitationProbability"):0}%":row is null?"超出預報範圍，天氣未知":"預報已過期或缺少資料，天氣未知";
            days.Add(item);
        }
        return new JsonObject{["source"]=mode!="direct"?"Open-Meteo / Zetaris":"Open-Meteo / direct",["dataSource"]=mode,["status"]=available==days.Count?"ok":available>0?"partial":"unavailable",["days"]=days,["rainy"]=anyRain,["summary"]=string.Join(" / ",days.Select(d=>d!.Str("date")+" "+d.Str("summary"))),["fetchedAt"]=records.FirstOrDefault()?.Str("fetchedAt")??"",["attribution"]="Weather data by Open-Meteo · CC BY 4.0"};
    }
}
