using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>A public, attributed discovery collection. No inferred ratings or unverified arrival instructions.</summary>
public sealed class HiddenGemService(TravelDataContext travel,ProjectPaths paths) {
    JsonArray Places(){
        if(travel.Warehouse)return travel.Snapshot?.Gems??throw new ServiceException("Zetaris hidden gems have not been imported.");
        var path=Path.Combine(paths.RepoRoot,"infra","zetaris","data","hidden-gems.json");
        if(!File.Exists(path))throw new ServiceException("Hidden gems have not been imported. Run scripts/import_hidden_gems.py.");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject().Array("places")??throw new ServiceException("Hidden gem data is invalid.");
    }
    public JsonArray PublicPlaces=>Places();
    public JsonObject Search(double lat,double lng,int radius,string text,CancellationToken token){
        token.ThrowIfCancellationRequested();
        var origin=new JsonObject{["lat"]=lat,["lng"]=lng};
        var records=Places().OfType<JsonObject>().Where(p=>text.Split(' ',StringSplitOptions.RemoveEmptyEntries).All(t=>(p.Str("name")+" "+p.Str("description")).Contains(t,StringComparison.InvariantCultureIgnoreCase)))
            .Select(p=>(Place:p,Meters:RouteService.Distance(origin,p))).Where(p=>p.Meters<=radius).OrderBy(p=>p.Meters).Take(80);
        var found=new JsonArray();foreach(var (place,meters) in records){token.ThrowIfCancellationRequested();var value=place.DeepClone().AsObject();value["distanceMeters"]=Math.Round(meters);found.Add(value);}
        return new(){["places"]=found,["source"]=travel.Warehouse?"Wikivoyage / Zetaris":"Wikivoyage / local collection",["dataSource"]=travel.Mode,
            ["attribution"]="Wikivoyage contributors · CC BY-SA 4.0; photos retain their individual Commons licenses",["message"]="Discovery candidates with unverified popularity and opening status. Descriptions are not user reviews."};
    }
    public JsonObject Media(string id){
        var place=Places().OfType<JsonObject>().FirstOrDefault(p=>p.Str("id")==id)??throw new ServiceException("The imported hidden gem was not found.");
        return new(){["photos"]=place.Array("photos")?.DeepClone()??new JsonArray(),["source"]="Wikimedia Commons / community collection",["nearbyRadiusMeters"]=150};
    }
}
