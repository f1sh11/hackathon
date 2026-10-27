using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace SydneyBuddy.Api;

/// User-supplied lodging/storage evidence. No inferred booking or storage availability.
public static class RouteLogistics
{
    public static bool IsTask(JsonObject p)=>p.Str("activityType") is "bag-drop" or "bag-pickup";
    public static bool IsDefaultEndpoint(JsonObject p)=>RouteService.Distance(p,new JsonObject{["lat"]=-33.8611,["lng"]=151.2126})<5 && p.Str("name") is "环形码头" or "環形碼頭" or "Circular Quay";
    public static bool MentionsHeavy(string text)=>!Regex.IsMatch(text,@"行李不多|沒有大件行李|没有大件行李|不帶行李|不带行李|no (?:heavy |large )?luggage|without luggage",RegexOptions.IgnoreCase)&&Regex.IsMatch(text,@"行李(?:比較多|比较多|很多|較多|较多)|大件行李|很多行李|多件行李|heavy luggage|lots of luggage|many suitcases|large suitcases",RegexOptions.IgnoreCase);
    public static JsonObject Point(JsonObject value)
    {
        var lat=value.Number("lat",double.NaN);var lng=value.Number("lng",double.NaN);
        if(!double.IsFinite(lat)||!double.IsFinite(lng)||lat is < -37.5 or > -28||lng is <141 or >154||string.IsNullOrWhiteSpace(value.Str("name"))||value.Str("name").Length>120)
            throw new ServiceException("請提供飯店或行李寄放點的有效名稱與 NSW 座標；不會把地區名稱當成已確認寄放服務。");
        var p=(JsonObject)value.DeepClone();
        foreach(var key in new[]{"storageConfirmed","costKnown"})if(p[key] is not null&&(p[key] is not JsonValue flag||!flag.TryGetValue<bool>(out _)))throw new ServiceException("行李寄放確認狀態格式無效。");
        foreach(var key in new[]{"openMinute","closeMinute"})if(p[key] is not null&&(!double.IsFinite(p.Number(key,double.NaN))||p.Number(key)!=Math.Truncate(p.Number(key))||p.Number(key)<0||p.Number(key)>1440))throw new ServiceException("行李寄放服務時間無效。");
        if(p.Int("openMinute",0)>=p.Int("closeMinute",1440))throw new ServiceException("寄放地點的取件時間必須晚於開門時間。");
        if(p["cost"] is not null&&(!double.IsFinite(p.Number("cost",double.NaN))||p.Number("cost") is <0 or >100000))throw new ServiceException("行李寄放費用無效。");
        return p;
    }
    public static void Normalize(JsonObject p)
    {
        if(p.Object("hotel") is {} hotel&&p.Str("accommodationStatus")=="booked"){
            foreach(var key in new[]{"start","end"})if(p.Object(key) is {} old&&IsDefaultEndpoint(old)&&!p.Bool(key+"IsExplicit"))p[key]=hotel.DeepClone();
        }
        var text=p.Str("request")+" "+p.Str("luggageNotes");
        if(p["heavyLuggage"] is null&&MentionsHeavy(text))p["heavyLuggage"]=true;
        foreach(var key in new[]{"heavyLuggage","luggageStored","startIsExplicit","endIsExplicit"})if(p[key] is not null&&(p[key] is not JsonValue v||!v.TryGetValue<bool>(out _)))throw new ServiceException("行李或起訖設定格式無效。");
        if(p.Object("luggageStorage") is {} storage)p["luggageStorage"]=Point(storage);
        if(p.Bool("heavyLuggage")&&p.Object("luggageStorage") is null&&p.Object("hotel") is {} fallback){
            var point=(JsonObject)fallback.DeepClone();point["storageConfirmed"]=false;p["luggageStorage"]=point;
        }
        if(p["luggageDays"] is not null){
            if(p["luggageDays"] is not JsonArray dates||dates.Count>15||dates.Any(d=>!DateOnly.TryParseExact(d?.ToString(),"yyyy-MM-dd",out _)))throw new ServiceException("請提供有效的行李寄放日期清單。");
        }
        if(p.Bool("luggageStored")&&p["luggageStoredDate"] is null)p["luggageStoredDate"]=p.Str("date");
        if(p["luggageStoredDate"] is not null&&!DateOnly.TryParseExact(p.Str("luggageStoredDate"),"yyyy-MM-dd",out _))throw new ServiceException("行李寄放日期無效。");
        if(p.Bool("heavyLuggage")&&p.Object("luggageStorage") is null)throw new ServiceException("行李較多，需要先確認飯店或寄放地點的位置，再安排早上寄放與離開前取件。請提供寄放點名稱／位置；尚未捏造寄放服務。");
    }
    public static JsonObject[] Tasks(JsonObject p,string date,bool before)
    {
        if(!p.Bool("heavyLuggage")||p.Array("luggageDays") is {} dates&&!dates.Any(d=>d?.ToString()==date)||before&&p.Bool("luggageStored")&&p.Str("luggageStoredDate",date)==date)return [];
        var storage=p.Object("luggageStorage")??throw new ServiceException("缺少行李寄放位置。");
        var point=Point(storage);var type=before?"bag-drop":"bag-pickup";
        var confirmed=storage.Bool("storageConfirmed");var knownHours=storage["openMinute"] is not null&&storage["closeMinute"] is not null;
        return [new JsonObject{["id"]="logistics:"+type+":"+date,["name"]=(before?"寄放行李 · ":"取回行李 · ")+point.Str("name"),
            ["lat"]=point.Number("lat"),["lng"]=point.Number("lng"),["activityType"]=type,["category"]="設施",["indoor"]=false,["indoorKnown"]=false,
            ["visitMinutes"]=before?15:10,["openMinute"]=storage.Int("openMinute",0),["closeMinute"]=storage.Int("closeMinute",1440),["hoursVerified"]=knownHours,
            ["storageConfirmed"]=confirmed,["cost"]=before?storage.Number("cost"):0,["costKnown"]=!before||storage["cost"] is not null&&storage.Bool("costKnown"),
            ["source"]=storage.Str("source","User-supplied storage location"),["sourceURL"]=storage.Str("sourceURL"),
            ["note"]=confirmed?"使用者提供的寄放服務資訊；取件需在服務時間內，未代為預約。":"暫定寄放任務：只確認位置，尚未核實可寄放、服務時間、費用與容量；不代表已寄放或已訂妥。"}];
    }
    public static JsonArray Points(JsonObject profile)=>new(profile.Bool("heavyLuggage")&&profile.Object("luggageStorage") is {} storage?new JsonNode[]{storage.DeepClone()}:[]);
}
