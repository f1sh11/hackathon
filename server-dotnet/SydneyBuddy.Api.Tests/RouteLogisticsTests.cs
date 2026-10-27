using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;

public sealed class RouteLogisticsTests
{
    static JsonObject Point(string name,double lat=-33.8611)=>new(){["name"]=name,["lat"]=lat,["lng"]=151.2126};
    static JsonObject Raw()=>new(){["date"]="2026-10-28",["budget"]=500,["startMinute"]=600,["endMinute"]=1200};
    static JsonObject Place(string id,int duration=15)=>new(){["id"]=id,["name"]=id,["lat"]=-33.86112,["lng"]=151.21262,["visitMinutes"]=duration,["cost"]=0,["indoor"]=true,["category"]="文化",["note"]="test",["openMinute"]=0,["closeMinute"]=1440};
    static Task<JsonArray> Schedule(JsonObject profile,JsonObject[] places,PlanningContext? context=null)=>new RouteScheduler(new RouteService(new FakeExternal())).ScheduleDaysAsync(profile,places,new JsonObject(),new Dictionary<string,int>(),context??new("demo",new Dictionary<string,string>()));
    [Theory][InlineData("低精力",8000)][InlineData("中等精力",10000)][InlineData("高精力",15000)]
    public void EnergyReferencesNeverRaiseAnExplicitStepLimit(string energy,int expected){var p=Raw();p["energy"]=energy;Assert.Equal(expected,PlanValidator.Validate(p,[]).Int("maxSteps"));p["maxSteps"]=4500;Assert.Equal(4500,PlanValidator.Validate(p,[]).Int("maxSteps"));}
    [Fact]public async Task MoreThanFourShortVisitsAreAllowedEvenForLowEnergy(){
        var raw=Raw();raw["energy"]="低精力";raw["endMinute"]=1080;var places=Enumerable.Range(0,9).Select(i=>Place("mini-"+i,5)).ToArray();
        var days=await Schedule(PlanValidator.Validate(raw,places),places);
        Assert.True(days[0]!.Array("stops")!.Count>4);Assert.InRange(days[0]!.Int("estimatedSteps"),0,8000);
    }
    [Fact]public void BookedHotelRebasesDefaultsButPreservesExplicitAirport(){
        var raw=Raw();raw["start"]=Point("环形码头");raw["end"]=Point("环形码头");raw["hotel"]=Point("Booked hotel",-33.87);raw["accommodationStatus"]="booked";
        var p=PlanValidator.Validate(raw,[]);Assert.Equal("Booked hotel",p.Object("start")!.Str("name"));Assert.Equal("Booked hotel",p.Object("end")!.Str("name"));
        raw["startIsExplicit"]=true;raw["end"]=Point("Airport",-33.9357);p=PlanValidator.Validate(raw,[]);Assert.Equal("环形码头",p.Object("start")!.Str("name"));Assert.Equal("Airport",p.Object("end")!.Str("name"));
    }
    [Fact]public async Task HeavyLuggageIsDroppedFirstAndCollectedBeforeTheReturnLeg(){
        var raw=Raw();raw["request"]="行李比較多，早上先寄放";raw["hotel"]=Point("Hotel",-33.862);raw["accommodationStatus"]="booked";
        var storage=Point("Station storage",-33.8615);storage["storageConfirmed"]=true;storage["openMinute"]=540;storage["closeMinute"]=1140;storage["cost"]=10;storage["costKnown"]=true;raw["luggageStorage"]=storage;
        var places=new[]{Place("museum")};var p=PlanValidator.Validate(raw,places);var days=await Schedule(p,places);var d=days[0]!.AsObject();var stops=d.Array("stops")!.OfType<JsonObject>().ToArray();
        Assert.Equal("bag-drop",stops[0].Str("activityType"));Assert.Equal("bag-pickup",stops[^1].Str("activityType"));Assert.Equal(stops[0].Number("lat"),stops[^1].Number("lat"));
        Assert.Equal(0,stops[0].Int("activitySteps"));Assert.Equal(stops.Length+1,d.Array("legs")!.Count);Assert.Equal(10,d.Number("estimatedCost"));Assert.True(string.CompareOrdinal(stops[0].Str("arrival"),"12:00")<0);
        Assert.Equal("Hotel",d.Object("end")!.Str("name"));Assert.True(string.CompareOrdinal(stops[^1].Str("departure"),d.Str("finishTime"))<=0);
    }
    [Fact]public async Task HotelLocationAloneDoesNotVerifyStorage(){
        var raw=Raw();raw["heavyLuggage"]=true;raw["hotel"]=Point("Hotel");raw["accommodationStatus"]="booked";
        var p=PlanValidator.Validate(raw,[]);var days=await Schedule(p,[Place("museum")]);
        Assert.False(days[0]!.Array("stops")![0]!.Bool("storageConfirmed"));Assert.Equal("partial",days[0]!.Str("recommendationStatus"));
        Assert.Contains(days[0]!.Array("warnings")!,w=>w!.ToString().Contains("服務尚未確認"));
    }
    [Fact]public void ARegionNameWithoutCoordinatesCannotBecomeAStorageService(){var raw=Raw();raw["heavyLuggage"]=true;raw["luggageStorage"]=new JsonObject{["name"]="Central"};Assert.Throws<ServiceException>(()=>PlanValidator.Validate(raw,[]));}
    [Fact]public void HeavyLuggageWithoutAStorageLocationRequiresClarification(){var raw=Raw();raw["request"]="heavy luggage";Assert.Contains("寄放",Assert.Throws<ServiceException>(()=>PlanValidator.Validate(raw,[])).Message);Assert.False(RouteLogistics.MentionsHeavy("行李不多"));}
    [Fact]public async Task StoredBagsAreNotDroppedAgainAndCollectionRemainsRequired(){
        var raw=Raw();raw["heavyLuggage"]=true;raw["luggageStored"]=true;raw["luggageStorage"]=Point("Storage");var p=PlanValidator.Validate(raw,[]);
        var days=await Schedule(p,[Place("museum")]);var stops=days[0]!.Array("stops")!.OfType<JsonObject>().ToArray();Assert.DoesNotContain(stops,s=>s.Str("activityType")=="bag-drop");Assert.Equal("bag-pickup",stops[^1].Str("activityType"));
        Assert.Equal(1,RouteLogistics.Tasks(p,"2026-10-29",true).Length);
    }
    [Fact]public async Task ClosingStorageCannotBeIgnoredToFitALateRequiredVisit(){
        var raw=Raw();raw["heavyLuggage"]=true;var storage=Point("Storage");storage["openMinute"]=540;storage["closeMinute"]=660;storage["storageConfirmed"]=true;raw["luggageStorage"]=storage;
        var place=Place("required");place["openMinute"]=900;raw["mustVisit"]=new JsonArray("required");var p=PlanValidator.Validate(raw,[place]);
        await Assert.ThrowsAsync<ServiceException>(()=>Schedule(p,[place]));
    }
    [Fact]public async Task SameActivitiesPreferFewerWalkingMetersOverAReversedDetour(){
        var raw=Raw();raw["endMinute"]=690;var a=Place("a",5);a["lat"]=-33.8613;var b=Place("b",5);b["lat"]=-33.8615;raw["mustVisit"]=new JsonArray("a","b");var p=PlanValidator.Validate(raw,[a,b]);
        var points=new[]{p.Object("start")!,a,b};var roads=new JsonArray();
        foreach(var from in points)foreach(var to in points)if(WalkingEvidence.Key(from,from)!=WalkingEvidence.Key(to,to)){
            var meters=from.Str("id")=="a"&&to.Str("id")=="b"?900:75;
            roads.Add(new JsonObject{["from"]=from.DeepClone(),["to"]=to.DeepClone(),["meters"]=meters,["seconds"]=60,["fetchedAt"]=DateTimeOffset.UtcNow.ToString("O")});
        }
        var result=await Schedule(p,[a,b],new("demo",new Dictionary<string,string>()){Walking=new WalkingEvidence(roads)});
        Assert.Equal("b",result[0]!.Array("stops")![0]!.Str("id"));Assert.Equal(225,result[0]!.Object("routeOptimization")!.Number("walkingMeters"));
    }
    [Fact]public void UnbookedLodgingFallbackNeverSelectsAHotelOrClaimsAvailability(){
        var p=PlanValidator.Validate(Raw(),[]);var original=p.Object("end")!.ToJsonString();var stay=Place("stay");stay["category"]="住宿";
        var suggestions=AccommodationSuggestions.Select(p,[stay],new JsonArray(),new JsonArray());Assert.Single(suggestions);Assert.Equal("unbooked",suggestions[0]!.Str("bookingStatus"));Assert.False(suggestions[0]!.Bool("availabilityVerified"));Assert.Equal(original,p.Object("end")!.ToJsonString());
        p["accommodationStatus"]="booked";Assert.Empty(AccommodationSuggestions.Select(p,[stay],new(),new()));
    }
    [Fact]public void CustomMealTimesRemainSeparateSlots(){var p=PlanValidator.Validate(Raw(),[]);p["mealTimes"]=new JsonObject{["lunch"]=780,["dinner"]=1110};MealTiming.Validate(p);var slots=MealTiming.Slots(p);Assert.Equal(780,slots.Single(s=>s.Name=="lunch").Target);Assert.Equal(1110,slots.Single(s=>s.Name=="dinner").Target);p.Object("mealTimes")!["dinner"]="not a time";Assert.Throws<ServiceException>(()=>MealTiming.Validate(p));}
}
