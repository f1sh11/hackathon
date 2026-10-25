using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;
public sealed class ScheduleEditorTests:IDisposable {
    readonly CatalogCache cache=new();
    ScheduleEditorService Service()=>new(new PlaceCatalog(cache,new ProjectPaths(TestSupport.RepositoryRoot())));
    JsonObject Seed(int days=1)=>new(){["mode"]="demo",["title"]="測試行程",["profile"]=TestSupport.Profile(days),["createdAt"]="2026-10-23T09:00:00Z",["trace"]=new JsonArray(),["warnings"]=new JsonArray(),["weather"]=new JsonObject{["summary"]="晴天示例",["source"]="測試",["rainy"]=false}};
    JsonObject Build(int days=1,JsonObject? settings=null)=>Service().Build(new JsonObject{["trip"]=Seed(days),["settings"]=settings??new JsonObject()});
    JsonObject Recalculate(JsonObject trip,Action<JsonArray> edit){var s=trip.Object("schedule")!;var days=(JsonArray)s.Array("days")!.DeepClone();edit(days);return Service().Build(new JsonObject{["trip"]=trip.DeepClone(),["settings"]=s["settings"]!.DeepClone(),["days"]=days,["version"]=s.Int("version")});}
    [Fact] public void FirstDraftContainsLifeStopsAndDistinctDailyAttractions(){
        var trip=Build(3,new(){["luggage"]=true,["dailyBudget"]=500,["totalBudget"]=1500});var days=trip.Object("schedule")!.Array("days")!;
        Assert.Equal(3,days.Count);
        foreach(var d in days.OfType<JsonObject>()){var a=d.Array("activities")!.OfType<JsonObject>().ToArray();Assert.Contains(a,x=>x.Str("kind")=="meal");Assert.Contains(a,x=>x.Str("kind")=="restroom");Assert.Contains(a,x=>x.Str("kind")=="bag-drop");Assert.Contains(a,x=>x.Str("kind")=="bag-pickup");}
        var selected=days.OfType<JsonObject>().SelectMany(d=>d.Array("activities")!.OfType<JsonObject>()).Where(a=>a.Str("kind")=="attraction").Select(a=>a.Str("placeId")).ToArray();Assert.Equal(selected.Length,selected.Distinct().Count());
        TripContractValidator.Validate(trip);
    }
    [Fact] public void QueueConsumesTimeButVisitDurationDoesNotDirectlyConsumeIt(){
        var trip=Build(settings:new(){["dailyBudget"]=500,["maxSteps"]=60000});
        var a=trip.Object("schedule")!.Array("days")![0]!.Array("activities")!.OfType<JsonObject>().First(x=>x.Str("kind")=="attraction");
        var modified=Recalculate(trip,ds=>ds[0]!.Array("activities")!.OfType<JsonObject>().First(x=>x.Str("id")==a.Str("id"))["queueMinutes"]=35);
        var day=modified.Object("schedule")!.Array("days")![0]!;Assert.Equal(35,day.Int("queueMinutes"));Assert.Equal(day.Int("transportMinutes")+35,day.Int("timeUsed"));
        var longer=Recalculate(modified,ds=>ds[0]!.Array("activities")!.OfType<JsonObject>().First(x=>x.Str("id")==a.Str("id"))["duration"]=90);
        Assert.Equal(day.Int("timeUsed"),longer.Object("schedule")!.Array("days")![0]!.Int("timeUsed"));
    }
    [Fact] public void ManualOrderAndUnknownQueueArePreserved(){var trip=Build(settings:new(){["dailyBudget"]=500});var changed=Recalculate(trip,ds=>{var list=ds[0]!.Array("activities")!;var a=list[0]!.DeepClone();list.RemoveAt(0);list.Add(a);});Assert.True(changed.Object("schedule")!.Array("days")![0]!.Bool("unknownQueue"));var before=trip.Object("schedule")!.Array("days")![0]!.Array("activities")![0]!.Str("id");Assert.Equal(before,changed.Object("schedule")!.Array("days")![0]!.Array("activities")!.Last()!.Str("id"));}
    [Fact] public void ResizingOrMovingPastClosingHoursCreatesBlockingIssue(){var trip=Build(settings:new(){["dailyBudget"]=500});var changed=Recalculate(trip,ds=>{var a=ds[0]!.Array("activities")!.OfType<JsonObject>().First(x=>x.Str("kind")=="attraction");a["fixedStart"]=1430;a["duration"]=60;});Assert.Contains(changed.Object("schedule")!.Array("issues")!.OfType<JsonObject>(),x=>x.Str("code")=="hours"&&x.Bool("blocking"));}
    [Fact] public void ReversedLuggageTasksAreRejectedAsConflict(){var trip=Build(settings:new(){["luggage"]=true,["dailyBudget"]=500});var changed=Recalculate(trip,ds=>{var list=ds[0]!.Array("activities")!;var drop=list.OfType<JsonObject>().First(x=>x.Str("kind")=="bag-drop");list.Remove(drop);list.Add(drop);});Assert.Contains(changed.Object("schedule")!.Array("issues")!.OfType<JsonObject>(),x=>x.Str("code")=="luggage"&&x.Bool("blocking"));}
    [Fact] public void HotelCostsArePerNightAndUpdateAdjacentEndpoints(){var trip=Build(3,new(){["dailyBudget"]=500,["totalBudget"]=2000});var result=Recalculate(trip,ds=>{foreach(var day in ds.OfType<JsonObject>()){day.Object("stay")!["cost"]=120;day.Object("stay")!["lat"]=-33.87;} });var days=result.Object("schedule")!.Array("days")!.OfType<JsonObject>().ToArray();Assert.Equal(-33.87,days[0].Object("end")!.Number("lat"));Assert.Equal(-33.87,days[1].Object("start")!.Number("lat"));var activityCost=days.Sum(d=>d.Array("activities")!.OfType<JsonObject>().Sum(a=>a.Number("cost")));Assert.Equal(activityCost+240,result.Object("schedule")!.Number("totalCost"));}
    [Fact] public void SpecifiedStartCannotBeSilentlyShiftedWhenArrivalIsTooLate(){var trip=Build();var changed=Recalculate(trip,ds=>ds[0]!.Array("activities")!.OfType<JsonObject>().First()["fixedStart"]=0);Assert.Contains(changed.Object("schedule")!.Array("issues")!.OfType<JsonObject>(),x=>x.Str("code")=="appointment"&&x.Bool("blocking"));}
    [Fact] public void MealAllowanceIsNotDuplicatedByTheLegacyFoodDistrictEstimate(){var trip=Build();var state=trip.Object("schedule")!;Assert.Equal(0,state.Array("candidates")!.OfType<JsonObject>().First(p=>p.Str("id")=="rocks").Number("cost"));Assert.Contains(state.Array("days")![0]!.Array("activities")!.OfType<JsonObject>(),a=>a.Str("kind")=="meal"&&a.Number("cost")>0);}
    [Fact] public void InvalidNumbersUnknownPlacesAndCrossProviderTripsFail(){Assert.Throws<ServiceException>(()=>Build(settings:new(){["timeLimit"]=-1}));var seed=Seed();seed["mode"]="live";Assert.Throws<ServiceException>(()=>Service().Build(new(){["trip"]=seed}));var trip=Build();Assert.Throws<ServiceException>(()=>Recalculate(trip,ds=>ds[0]!.Array("activities")!.OfType<JsonObject>().First(x=>x.Str("kind")=="attraction")["placeId"]="invented"));}
    [Fact] public void LowLimitsProduceExplicitWarningsAndDoNotChangeUserLimits(){var trip=Build(settings:new(){["dailyBudget"]=1,["totalBudget"]=1,["timeLimit"]=0,["maxSteps"]=500});Assert.Equal(500,trip.Object("schedule")!.Object("settings")!.Int("maxSteps"));Assert.Contains(trip.Object("schedule")!.Array("issues")!.OfType<JsonObject>(),x=>x.Str("code")=="budget");}
    public void Dispose()=>cache.Dispose();
    [Fact] public void ExplicitDayAnchorsSurviveRecalculationWithoutMovingNextDayHotel() {
        var trip=Build(2,new(){["dailyBudget"]=500,["totalBudget"]=2000});
        var before=trip.Object("schedule")!.Array("days")![1]!.Object("start")!.ToJsonString();
        var result=Recalculate(trip,days=>{var d=days[0]!.AsObject();d["startOverride"]=true;d["endOverride"]=true;d["start"]=new JsonObject{["name"]="当天出发点",["lat"]=-33.868,["lng"]=151.209};d["end"]=new JsonObject{["name"]="当天返回点",["lat"]=-33.86,["lng"]=151.21};d["startMinute"]=660;});
        var after=result.Object("schedule")!.Array("days")!;
        Assert.Equal("当天出发点",after[0]!.Object("start")!.Str("name"));Assert.Equal("当天返回点",after[0]!.Object("end")!.Str("name"));Assert.True(after[0]!.Bool("startOverride"));Assert.Equal(660,after[0]!.Int("startMinute"));
        Assert.Equal(before,after[1]!.Object("start")!.ToJsonString());
    }
}
