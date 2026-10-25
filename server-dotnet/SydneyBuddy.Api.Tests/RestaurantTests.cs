using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
namespace SydneyBuddy.Api.Tests;
public sealed class RestaurantTests {
    public static JsonObject Input()=>new(){["sourceType"]="official_menu",["sourceUrl"]="https://example.com/menu",["sourceName"]="Test supplied menu",["author"]="Test author",["license"]="test-permission",["observedAt"]=DateTimeOffset.UtcNow.ToString("O"),["sourceText"]="Alex recommends noodles. AUD 40 for 2 people.",["menuItems"]=new JsonArray(new JsonObject{["name"]="Noodles",["amount"]=19.95m,["currency"]="AUD",["channel"]="dine_in"}),["reportedSpends"]=new JsonArray(new JsonObject{["amount"]=40m,["currency"]="AUD",["people"]=2,["quote"]="AUD 40 for 2 people.",["author"]="Alex"}),["recommendations"]=new JsonArray(new JsonObject{["dish"]="noodles",["quote"]="Alex recommends noodles.",["author"]="Alex"})};
    [Fact] public void PreservesPriceTypesAndNeverInfersAnAverageOrRating(){var result=RestaurantEvidence.Normalize(Input());Assert.Equal("menu_item_price",result.Array("menuItems")![0]!.Str("priceType"));Assert.Equal("reported_spend",result.Array("reportedSpends")![0]!.Str("priceType"));Assert.Null(result["rating"]);Assert.Null(result["perPerson"]);}
    [Fact] public void RejectsUserSpendMasqueradingAsMenuAndInventedQuote(){var input=Input();input["sourceType"]="user_import";Assert.Throws<ServiceException>(()=>RestaurantEvidence.Normalize(input));input=Input();input.Array("recommendations")![0]!["quote"]="invented";Assert.Throws<ServiceException>(()=>RestaurantEvidence.Normalize(input));input=Input();input.Array("reportedSpends")![0]!["amount"]=400m;Assert.Throws<ServiceException>(()=>RestaurantEvidence.Normalize(input));}
    [Fact] public void ParsesUploadedStructuredMenuWithoutNetwork(){var result=RestaurantEvidence.ParseMenu(new(){["content"]="""<html><script type="application/ld+json">{"@type":"Menu","hasMenuSection":{"@type":"MenuSection","hasMenuItem":{"@type":"MenuItem","name":"Noodles","offers":{"price":"19.95","priceCurrency":"AUD"}}}}</script></html>"""});Assert.Single(result.Array("menuItems")!);Assert.Equal(19.95,result.Array("menuItems")![0]!.Number("amount"));}
    static JsonObject Point(){var normalized=RestaurantEvidence.Normalize(Input());normalized["budgetEstimate"]=new JsonObject{["status"]="unknown"};return new(){["restaurantData"]=normalized};}
    static JsonObject Selection(JsonObject p)=>new(){["people"]=3,["items"]=new JsonArray(new JsonObject{["id"]=p.Object("restaurantData")!.Array("menuItems")![0]!.Str("id"),["quantity"]=2})};
    [Fact] public void CalculatesOnlySelectedMenuItemsAndRoundsUpCents(){var point=Point();var result=RestaurantService.Calculate(point,Selection(point));Assert.Equal(39.90,result.Number("total"));Assert.Equal(13.30,result.Number("perPerson"));Assert.Equal(3,result.Int("people"));Assert.Single(result.Array("basis")!);}
    [Fact] public void RejectsStaleOrMixedChannelPrices(){var point=Point();var request=Selection(point);request["channel"]="delivery";Assert.Throws<ServiceException>(()=>RestaurantService.Calculate(point,request));request=Selection(point);point.Object("restaurantData")!.Array("menuItems")![0]!.Object("source")!["observedAt"]=DateTimeOffset.UtcNow.AddDays(-91).ToString("O");Assert.Throws<ServiceException>(()=>RestaurantService.Calculate(point,request));}
}
public sealed class RestaurantApiTests(ApiFixture fixture):IClassFixture<ApiFixture>{
    [Fact] public async Task ImportEstimateAndScheduleUseDatabaseEvidenceRatherThanClientPrice(){
        var root=fixture.TemporaryRoot;Directory.CreateDirectory(Path.Combine(root,"infra","poi","data"));Directory.CreateDirectory(Path.Combine(root,"infra","zetaris","data"));
        await File.WriteAllTextAsync(Path.Combine(root,"infra","poi","data","places.json"),"""{"schemaVersion":1,"source":"Overture Maps","bbox":[151.1,-33.95,151.3,-33.8],"places":[]}""");
        await File.WriteAllTextAsync(Path.Combine(root,"infra","zetaris","data","hidden-gems.json"),"{\"places\":[]}");
        await File.WriteAllTextAsync(Path.Combine(root,"infra","zetaris","data","osm-map-places.json"),"""{"places":[{"id":"osm:node:456","name":"Test Restaurant","category":"美食","poiKind":"restaurant","lat":-33.865,"lng":151.212,"source":"OpenStreetMap"}]}""");
        async Task<JsonObject> Post(string path,JsonObject body){var response=await fixture.Client.PostAsJsonAsync(path,body);var raw=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,raw);return JsonNode.Parse(raw)!.AsObject();}
        var input=RestaurantTests.Input();input["placeId"]="osm:node:456";input["placeName"]="Test Restaurant";input["lat"]=-33.865;input["lng"]=151.212;
        var imported=await Post("/restaurants/import",input);var id=imported.Str("id");Assert.Equal("unknown",imported.Object("restaurantData")!.Object("budgetEstimate")!.Str("status"));Assert.Equal(0,imported.Number("cost"));
        var menuId=imported.Object("restaurantData")!.Array("menuItems")![0]!.Str("id");
        var updated=await Post("/restaurants/estimate",new(){["placeId"]=id,["people"]=2,["items"]=new JsonArray(new JsonObject{["id"]=menuId,["quantity"]=2})});Assert.Equal(19.95,updated.Number("cost"));
        var search=await Post("/poi/search",new(){["provider"]="osm",["lat"]=-33.865,["lng"]=151.212,["radius"]=500,["category"]="food",["query"]="Test Restaurant"});Assert.Single(search.Array("places")!);Assert.Equal(id,search.Array("places")![0]!.Str("id"));
        await using var factory=fixture.CreateFactory();using var scope=factory.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService<ScheduleEditorService>();
        var trip=new JsonObject{["mode"]="demo",["title"]="Test",["profile"]=TestSupport.Profile(),["createdAt"]="2026-10-25T00:00:00Z",["trace"]=new JsonArray(),["warnings"]=new JsonArray(),["weather"]=new JsonObject{["summary"]="Test",["source"]="Test",["rainy"] =false}};
        var prepared=service.Build(new(){["trip"]=trip,["candidates"]=new JsonArray(updated.DeepClone())});var schedule=prepared.Object("schedule")!;var days=schedule.Array("days")!.DeepClone().AsArray();
        var meals=days[0]!.Array("activities")!;meals.Clear();meals.Add(new JsonObject{["id"]="test-meal",["kind"]="meal",["placeId"]=id,["duration"]=45,["queueMinutes"]=20,["cost"]=1,["locked"]=false});
        var forged=updated.DeepClone().AsObject();forged.Object("restaurantData")!.Object("budgetEstimate")!["perPerson"]=999;
        var planned=service.Build(new(){["trip"]=prepared,["candidates"]=new JsonArray(forged),["settings"]=schedule["settings"]!.DeepClone(),["days"]=days});
        var meal=planned.Object("schedule")!.Array("days")![0]!.Array("activities")!.OfType<JsonObject>().Single(x=>x.Str("id")=="test-meal");Assert.Equal(19.95,meal.Number("cost"));Assert.Equal("selected_menu_per_person",meal.Str("costBasis"));Assert.Equal(20,planned.Object("schedule")!.Array("days")![0]!.Int("queueMinutes"));Assert.False(meal.Bool("hoursVerified"));
        var reference=meal.Str("restaurantEstimateId");Assert.NotEmpty(reference);
        await Post("/restaurants/estimate",new(){["placeId"]=id,["people"]=1,["items"]=new JsonArray(new JsonObject{["id"]=menuId,["quantity"]=2})});
        var revised=service.Build(new(){["trip"]=planned,["settings"]=planned.Object("schedule")!["settings"]!.DeepClone(),["days"]=planned.Object("schedule")!["days"]!.DeepClone()});
        var preserved=revised.Object("schedule")!.Array("days")![0]!.Array("activities")!.OfType<JsonObject>().Single(x=>x.Str("id")=="test-meal");Assert.Equal(19.95,preserved.Number("cost"));Assert.Equal(reference,preserved.Str("restaurantEstimateId"));
    }
}
