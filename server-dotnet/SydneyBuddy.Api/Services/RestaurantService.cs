using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
namespace SydneyBuddy.Api;

public static class RestaurantEvidence {
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
    public static string Url(string value){if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||uri.IsLoopback||value.Length>2000)throw new ServiceException("The source must be a public HTTPS URL.");return value;}
    public static string Date(string value){if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)||date>DateTimeOffset.UtcNow.AddDays(1))throw new ServiceException("Provide a valid observation date that is not in the future.");return date.ToUniversalTime().ToString("O");}
    public static decimal Money(JsonObject input,string key){if(input[key] is not JsonValue value||!value.TryGetValue<decimal>(out var amount)||amount<0||amount>100000||decimal.Round(amount,2)!=amount)throw new ServiceException("The amount must be non-negative, with up to two decimal places.");return amount;}
    public static JsonObject Normalize(JsonObject input){
        var type=input.Str("sourceType");if(type is not ("official_menu" or "licensed_dataset" or "user_import"))throw new ServiceException("This restaurant data source is not supported.");
        var source=new JsonObject{["type"]=type,["url"]=Url(input.Str("sourceUrl")),["name"]=input.Str("sourceName"),["author"]=input.Str("author"),["observedAt"]=Date(input.Str("observedAt")),["license"]=input.Str("license")};
        if(source.Str("name").Length is <1 or >200||source.Str("author").Length>200||source.Str("license").Length is <1 or >500)throw new ServiceException("Provide the source name and the basis for permission to use it.");
        var result=new JsonObject{["source"]=source,["menuItems"]=new JsonArray(),["reportedSpends"]=new JsonArray(),["recommendations"]=new JsonArray(),["reviews"]=new JsonArray()};
        foreach(var field in new[]{"menuItems","reportedSpends","recommendations","reviews"}){
            var rows=input.Array(field)??new();if(rows.Count>100)throw new ServiceException("You can import up to 100 records per category.");
            foreach(var node in rows){if(node is not JsonObject row)throw new ServiceException("The restaurant data format is invalid.");var entry=new JsonObject{["source"]=source.DeepClone()};
                if(field=="menuItems"){
                    if(type=="user_import")throw new ServiceException("An individual's reported spend cannot be treated as an official menu price.");
                    var name=row.Str("name").Trim();var currency=row.Str("currency").ToUpperInvariant();var channel=row.Str("channel");
                    if(name.Length is <1 or >200||!Regex.IsMatch(currency,"^[A-Z]{3}$")||channel is not ("dine_in" or "delivery" or "takeaway"))throw new ServiceException("Menu items require a dish name, currency and price channel.");
                    entry["name"]=name;entry["amount"]=Money(row,"amount");entry["currency"]=currency;entry["channel"]=channel;entry["unit"]="item";entry["priceType"]="menu_item_price";
                    entry["id"]="menu:"+Hash(source.Str("url")+"|"+name+"|"+currency+"|"+channel+"|"+source.Str("observedAt")+"|"+entry["amount"]!.ToJsonString());
                }else{
                    var quote=row.Str("quote");if(quote.Length is <1 or >2000||!input.Str("sourceText").Contains(quote,StringComparison.Ordinal))throw new ServiceException("Reviews and reported spending must have matching original evidence.");
                    entry["quote"]=quote;entry["author"]=row.Str("author",source.Str("author"));entry["date"]=row.Str("date").Length>0?Date(row.Str("date")):null;
                    if(entry.Str("author").Length is <1 or >200)throw new ServiceException("Provide the reviewer or recommender. If unknown, use 'Author not provided by source'.");
                    if(field=="recommendations"){var dish=row.Str("dish");if(dish.Length is <1 or >200||!quote.Contains(dish,StringComparison.Ordinal))throw new ServiceException("The recommended dish name must appear in the original evidence.");entry["dish"]=dish;}
                    if(field=="reportedSpends"){
                        var statedAmount=Money(row,"amount");if(!Regex.Matches(quote,@"[0-9]+(?:\.[0-9]{1,2})?").Any(m=>decimal.TryParse(m.Value,CultureInfo.InvariantCulture,out var value)&&value==statedAmount))throw new ServiceException("The reported spend amount must appear in the original evidence.");
                        entry["priceType"]="reported_spend";entry["currency"]=row.Str("currency").ToUpperInvariant();entry["amount"]=Money(row,"amount");
                        if(!Regex.IsMatch(entry.Str("currency"),"^[A-Z]{3}$"))throw new ServiceException("Reported spending requires a currency.");
                        var people=row.Number("people",double.NaN);if(!double.IsNaN(people)&&(people<1||people>100||people!=Math.Truncate(people)))throw new ServiceException("The number of diners is invalid.");entry["people"]=double.IsNaN(people)?null:(int)people;entry["includes"]=row.Str("includes")[..Math.Min(row.Str("includes").Length,500)];
                    }
                    entry["status"]="community_claim_unverified";
                }
                result.Array(field)!.Add(entry);
            }
        }
        if(new[]{"menuItems","reportedSpends","recommendations","reviews"}.All(f=>result.Array(f)!.Count==0))throw new ServiceException("Provide at least one menu item, reported spend or recommendation with evidence.");
        return result;
    }
    public static JsonObject ParseMenu(JsonObject input){
        var content=input.Str("content");if(content.Length is <1 or >150000)throw new ServiceException("Paste up to 150000 characters of official website HTML or JSON-LD.");
        var documents=new List<JsonNode>();
        if(content.TrimStart().StartsWith('{')||content.TrimStart().StartsWith('['))documents.Add(JsonNode.Parse(content)!);
        else foreach(Match match in Regex.Matches(content,"<script\\b[^>]*type\\s*=\\s*['\"]application/ld\\+json['\"][^>]*>(.*?)</script\\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(1))){try{documents.Add(JsonNode.Parse(match.Groups[1].Value)!);}catch(System.Text.Json.JsonException){}}
        var found=new JsonArray();
        void Walk(JsonNode? node){if(node is JsonArray array){foreach(var child in array)Walk(child);return;}if(node is not JsonObject obj)return;
            var types=obj["@type"] is JsonArray a?a.Select(x=>x?.ToString()).ToArray():new[]{obj.Str("@type")};
            if(types.Contains("MenuItem")&&obj["offers"] is JsonObject offer){var amount=offer["price"]?.ToString();if(decimal.TryParse(amount,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var price))found.Add(new JsonObject{["name"]=obj.Str("name"),["amount"]=price,["currency"]=offer.Str("priceCurrency"),["channel"]=input.Str("channel","dine_in")});}
            foreach(var child in obj)if(child.Key!="offers")Walk(child.Value);
        }
        foreach(var doc in documents)Walk(doc);
        return new(){["menuItems"]=found,["status"]=found.Count>0?"preview":"no-structured-menu",["message"]="Only the supplied structured menu is parsed. The website has not been fetched. Confirm the branch, channel and date."};
    }
}
public sealed class RestaurantStore(BuddyDbContext db){
    public static string Id(string venue)=>"restaurant:"+RestaurantEvidence.Hash(venue);
    public JsonArray Published()=>new(db.RestaurantVenues.AsNoTracking().OrderBy(x=>x.Id).Take(1000).AsEnumerable().Select(x=>(JsonNode)Expire(JsonNode.Parse(x.DataJson)!.AsObject())).ToArray());
    public JsonObject? Get(string id){var row=db.RestaurantVenues.AsNoTracking().SingleOrDefault(x=>x.Id==id);return row is null?null:Expire(JsonNode.Parse(row.DataJson)!.AsObject());}
    static JsonObject Expire(JsonObject point){var estimate=point.Object("restaurantData")?.Object("budgetEstimate");if(estimate?.Str("status")=="ready"&&!RestaurantService.Fresh(estimate)){estimate["status"]="stale";point["cost"]=0;}return point;}
    public JsonObject? GetEstimate(string id){var row=db.RestaurantEstimates.AsNoTracking().SingleOrDefault(x=>x.Id==id);return row is null?null:JsonNode.Parse(row.DataJson)!.AsObject();}
    public async Task SavePoint(JsonObject point,CancellationToken ct){var id=point.Str("id");var data=point.ToJsonString();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO restaurant_venues(id,data) VALUES({id},{data}::jsonb) ON CONFLICT(id) DO UPDATE SET data=EXCLUDED.data",ct);}
    public async Task<JsonObject> EstimateAsync(JsonObject input,CancellationToken ct){
        await using var tx=await db.Database.BeginTransactionAsync(ct);var id=input.Str("placeId");
        var row=await db.RestaurantVenues.FromSqlInterpolated($"SELECT * FROM restaurant_venues WHERE id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct)??throw new ServiceException("Restaurant data was not found.");
        var point=JsonNode.Parse(row.DataJson)!.AsObject();var estimate=RestaurantService.Calculate(point,input);var key=estimate.Str("id");var data=estimate.ToJsonString();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO restaurant_estimates(id,data) VALUES({key},{data}::jsonb)",ct);
        point.Object("restaurantData")!["budgetEstimate"]=estimate;point["cost"]=estimate.Str("currency")=="AUD"&&estimate.Str("channel")=="dine_in"?estimate["perPerson"]!.DeepClone():JsonValue.Create(0);
        await SavePoint(point,ct);await tx.CommitAsync(ct);return point;
    }
    public async Task<JsonObject> Import(JsonObject evidence,JsonObject point,CancellationToken ct){
        var venue=point.Str("matchedPlaceId",point.Str("id"));var data=evidence.ToJsonString();var id="restaurant-source:"+RestaurantEvidence.Hash(venue+data);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({venue}))",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO restaurant_imports(id,venue_id,data) VALUES({id},{venue},{data}::jsonb) ON CONFLICT(id) DO NOTHING",ct);
        var records=await db.RestaurantImports.AsNoTracking().Where(x=>x.VenueId==venue).ToListAsync(ct);var grouped=records.Select(x=>JsonNode.Parse(x.DataJson)!.AsObject()).OrderByDescending(x=>x.Object("source")!.Str("observedAt")).ToArray();
        var summary=new JsonObject();foreach(var field in new[]{"menuItems","reportedSpends","recommendations","reviews"})summary[field]=new JsonArray(grouped.SelectMany(x=>x.Array(field)!.OfType<JsonObject>()).Select(x=>(JsonNode)x.DeepClone()).ToArray());
        summary["priceObservations"]=summary["menuItems"]!.DeepClone();
        summary["menuItems"]=new JsonArray(summary.Array("menuItems")!.OfType<JsonObject>().GroupBy(x=>x.Object("source")!.Str("url")+"|"+x.Str("name")+"|"+x.Str("currency")+"|"+x.Str("channel")).Select(g=>(JsonNode)g.First().DeepClone()).ToArray());
        summary["budgetEstimate"]=new JsonObject{["status"]="unknown",["reason"]="No dishes are selected. Menu prices and reported spending are not automatically deducted from your budget."};
        var output=point.DeepClone().AsObject();output["id"]=Id(venue);output["matchedPlaceId"]=venue;output["source"]="Restaurant evidence";output["restaurantData"]=summary;output["cost"]=0;
        await SavePoint(output,ct);await tx.CommitAsync(ct);return output;
    }
}
public sealed class RestaurantService(RestaurantStore store,UnifiedPoiService poi){
    public static bool Fresh(JsonObject estimate)=>estimate.Array("basis") is JsonArray basis && basis.Count>0 && basis.OfType<JsonObject>().All(b=>DateTimeOffset.TryParse(b.Object("source")?.Str("observedAt"),out var observed)&&observed>=DateTimeOffset.UtcNow.AddDays(-90));
    public async Task<JsonObject> ImportAsync(JsonObject input,CancellationToken ct){
        if(input.Str("content").Length>0){input=input.DeepClone().AsObject();input["menuItems"]=RestaurantEvidence.ParseMenu(input)["menuItems"]!.DeepClone();}
        var evidence=RestaurantEvidence.Normalize(input);var lat=input.Number("lat",double.NaN);var lng=input.Number("lng",double.NaN);var name=input.Str("placeName");
        if(!OsmPoiProvider.ValidPoint(lat,lng)||name.Length is <1 or >80)throw new ServiceException("Provide the selected Sydney place and its coordinates.");
        var known=poi.Search(lat,lng,500,"all",name,ct).Array("places")!.OfType<JsonObject>().FirstOrDefault(p=>p.Str("id")==input.Str("placeId")||p.Array("sourceIds")?.Any(x=>x?.ToString()==input.Str("placeId"))==true)??throw new ServiceException("The restaurant branch could not be matched. Select a place on the map first.");
        if(known.Str("category")!="美食")throw new ServiceException("Only food and drink venues can be imported.");return await store.Import(evidence,known,ct);
    }
    public static JsonObject Calculate(JsonObject point,JsonObject request){
        var items=request.Array("items")??throw new ServiceException("Choose some dishes.");var people=request.Int("people");var channel=request.Str("channel","dine_in");var currency=request.Str("currency","AUD");
        if(people is <1 or >100||items.Count is <1 or >30)throw new ServiceException("Provide dishes and a valid number of diners.");
        var menu=point.Object("restaurantData")!.Array("menuItems")!.OfType<JsonObject>().ToArray();decimal cents=0;var basis=new JsonArray();var seen=new HashSet<string>();
        foreach(var node in items){if(node is not JsonObject choice||!seen.Add(choice.Str("id")))throw new ServiceException("Dishes cannot be duplicated. Adjust the quantity instead.");var row=menu.FirstOrDefault(x=>x.Str("id")==choice.Str("id"))??throw new ServiceException("The selected menu item was not found.");
            var observed=DateTimeOffset.Parse(row.Object("source")!.Str("observedAt"),CultureInfo.InvariantCulture);if(observed<DateTimeOffset.UtcNow.AddDays(-90))throw new ServiceException("The menu data is over 90 days old. Update prices before estimating.");
            if(row.Str("currency")!=currency||row.Str("channel")!=channel)throw new ServiceException("Different currencies or dine-in and delivery prices cannot be combined.");
            var qty=choice.Number("quantity",1);if(!double.IsFinite(qty)||qty<1||qty>30||qty!=Math.Truncate(qty))throw new ServiceException("The quantity is invalid.");var amount=RestaurantEvidence.Money(row,"amount");cents+=amount*100*(decimal)qty;basis.Add(new JsonObject{["menuItemId"]=row.Str("id"),["name"]=row.Str("name"),["quantity"]=(int)qty,["unitPrice"]=amount,["source"]=row["source"]!.DeepClone()});
        }
        if(cents>10000000m)throw new ServiceException("The estimated meal total exceeds the local limit.");
        return new(){["id"]="estimate:"+Guid.NewGuid().ToString("N"),["placeId"]=point.Str("id"),["priceType"]="estimated_per_person",["status"]="ready",["currency"]=currency,["channel"]=channel,["people"]=people,["total"]=cents/100,["perPerson"]=decimal.Ceiling(cents/people)/100,["basis"]=basis,["calculatedAt"]=DateTimeOffset.UtcNow.ToString("O"),["note"]="Includes selected dishes only. Drinks, surcharges and tips not provided are excluded. Per-person amounts are rounded up to the nearest cent."};
    }
    public Task<JsonObject> EstimateAsync(JsonObject input,CancellationToken ct)=>store.EstimateAsync(input,ct);
}
