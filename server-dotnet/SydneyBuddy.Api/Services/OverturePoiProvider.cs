using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>Search a versioned, real Overture snapshot locally. Never queries Overpass or a paid API.</summary>
public sealed class OverturePoiProvider(ProjectPaths paths,IConfiguration configuration):IPoiProvider {
    readonly object gate=new();
    string PathName=>configuration["Poi:OvertureFile"]??Path.Combine(paths.RepoRoot,"infra","poi","data","places.json");
    DateTime loadedAt;JsonObject? snapshot;
    public string Source=>"Overture Maps / local snapshot";
    public string Attribution=>"Overture Maps Foundation · CDLA-Permissive-2.0 / Apache-2.0";
    public bool UsesRemoteService=>false;
    JsonObject Snapshot(){lock(gate){
        var file=new FileInfo(PathName);
        if(!file.Exists)throw new ServiceException("尚未匯入開放商家資料，請先執行 scripts/import_overture_places.py 下載雪梨資料。");
        if(snapshot is not null&&loadedAt==file.LastWriteTimeUtc)return snapshot;
        try{
            if(file.Length>100_000_000)throw new ServiceException("開放商家資料過大，請縮小匯入範圍。");
            var data=JsonNode.Parse(File.ReadAllText(PathName))?.AsObject()??throw new FormatException();
            var bbox=data.Array("bbox");var points=data.Array("places");
            if(data.Int("schemaVersion")!=1||data.Str("source")!="Overture Maps"||bbox?.Count!=4||points is null||points.Count>100000)throw new FormatException();
            var values=bbox.Select(x=>x!.GetValue<double>()).ToArray();
            if(values.Any(x=>!double.IsFinite(x))||values[0]>=values[2]||values[1]>=values[3]||!OsmPoiProvider.ValidPoint(values[1],values[0])||!OsmPoiProvider.ValidPoint(values[3],values[2]))throw new FormatException();
            foreach(var p in points.OfType<JsonObject>())if(!p.Str("id").StartsWith("overture:")||!OsmPoiProvider.ValidPoint(p.Number("lat",double.NaN),p.Number("lng",double.NaN))||p.Str("name").Length==0)throw new FormatException();
            snapshot=data;loadedAt=file.LastWriteTimeUtc;return data;
        }catch(Exception e)when(e is IOException or System.Text.Json.JsonException or FormatException or InvalidOperationException){throw new ServiceException("開放商家資料檔案無效，請重新匯入，原有行程不受影響。");}
    }}
    public JsonObject Metadata {get{var s=Snapshot();return new(){["release"]=s.Str("release"),["downloadedAt"]=s.Str("downloadedAt"),["bbox"]=s["bbox"]!.DeepClone(),["totalPlaces"]=s.Array("places")!.Count,["localSnapshot"]=true};}}
    public Task<JsonArray> SearchAsync(double lat,double lng,int radius,string category,string text,CancellationToken token){
        token.ThrowIfCancellationRequested();var data=Snapshot();var b=data.Array("bbox")!.Select(x=>x!.GetValue<double>()).ToArray();
        var dy=radius/111320.0;var dx=dy/Math.Cos(lat*Math.PI/180);
        if(lng-dx<b[0]||lng+dx>b[2]||lat-dy<b[1]||lat+dy>b[3])throw new ServiceException("搜尋範圍超出目前匯入的雪梨資料區域，請移回市中心或擴大匯入範圍。");
        double Distance(JsonObject p){var a=(p.Number("lat")-lat)*Math.PI/180;var z=(p.Number("lng")-lng)*Math.PI/180;var h=Math.Pow(Math.Sin(a/2),2)+Math.Cos(lat*Math.PI/180)*Math.Cos(p.Number("lat")*Math.PI/180)*Math.Pow(Math.Sin(z/2),2);return 6371000*2*Math.Asin(Math.Sqrt(Math.Clamp(h,0,1)));}
        var tokens=text.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var found=data.Array("places")!.OfType<JsonObject>().Where(p=>p.Str("searchCategory")==category&&tokens.All(q=>CultureInfo.InvariantCulture.CompareInfo.IndexOf(p.Str("searchText",p.Str("name")),q,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0)).Select(p=>(Point:p,Meters:Distance(p))).Where(x=>x.Meters<=radius).OrderBy(x=>x.Meters).ThenBy(x=>x.Point.Str("id")).Take(80);
        var result=new JsonArray();foreach(var (point,meters) in found){token.ThrowIfCancellationRequested();var p=point.DeepClone().AsObject();p.Remove("searchText");p.Remove("searchCategory");p["distanceMeters"]=Math.Round(meters);p["source"]="Overture Maps";result.Add(p);}
        return Task.FromResult(result);
    }
}
