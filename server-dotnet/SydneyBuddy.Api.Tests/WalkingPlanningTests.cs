using System.Text.Json.Nodes;
using Xunit;
namespace SydneyBuddy.Api.Tests;
public sealed class WalkingPlanningTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"walking-planning-"+Guid.NewGuid());
    readonly PlaceCatalog catalog;
    public WalkingPlanningTests(){Directory.CreateDirectory(Path.Combine(root,"server"));File.Copy(Path.Combine(TestSupport.RepositoryRoot(),"server","places.json"),Path.Combine(root,"server","places.json"));catalog=new(new CatalogCache(),new ProjectPaths(root));}
    JsonArray Matrix(IEnumerable<JsonObject> points,Func<JsonObject,JsonObject,double>? seconds=null){var ps=points.DistinctBy(p=>WalkingEvidence.Key(p,p)).ToArray();var items=new JsonArray();foreach(var a in ps)foreach(var b in ps)if(WalkingEvidence.Key(a,a)!=WalkingEvidence.Key(b,b))items.Add(new JsonObject{["from"]=a.DeepClone(),["to"]=b.DeepClone(),["meters"]=50,["seconds"]=seconds?.Invoke(a,b)??60,["fetchedAt"]=DateTimeOffset.UtcNow.ToString("O")});return items;}
    JsonArray HistoricalMatrix(IEnumerable<JsonObject> points){var items=Matrix(points);foreach(var item in items.OfType<JsonObject>()){item.Remove("fetchedAt");item["recordedReplay"]=true;item["recordedDate"]="2026-10-23";item["source"]="Valhalla / OpenStreetMap recorded replay";}return items;}
    JsonObject Profile(int days=1){var p=TestSupport.Profile(days);p["date"]=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney")).ToString("yyyy-MM-dd");p["maxSteps"]=60000;p["endMinute"]=1439;return PlanValidator.Validate(p,catalog.Get());}
    [Fact] public void EndpointDuplicateDoesNotRemoveNearbyRestaurantOrExplicitMustSee() {
        var p=Profile();p["start"]=new JsonObject{["name"]="环形码头",["lat"]=-33.8611,["lng"]=151.2126};p["end"]=p["start"]!.DeepClone();
        var same=new JsonObject{["id"]="quay",["name"]="Circular Quay",["lat"]=-33.86114,["lng"]=151.21265,["category"]="风景"};
        var meal=(JsonObject)same.DeepClone();meal["id"]="meal";meal["name"]="Quay Restaurant";meal["activityType"]="meal";
        Assert.True(RecommendationQuality.DuplicatesEndpoint(same,p));Assert.False(RecommendationQuality.DuplicatesEndpoint(meal,p));
        Assert.DoesNotContain(RecommendationQuality.DailyPool([same,meal],p,new HashSet<string>(),new Dictionary<string,int>()),x=>x.Str("id")=="quay");
        Assert.Contains(RecommendationQuality.DailyPool([same,meal],p,new HashSet<string>{"quay"},new Dictionary<string,int>()),x=>x.Str("id")=="quay");
    }
    [Fact] public void FullDayWishDoesNotSilentlyBecomeReadyHalfDayEvenForRelaxedTraveler() {
        var p=Profile();p["dayStyle"]="fullDay";p["rhythm"]="轻松随意";
        var day=new JsonObject{["stops"]=new JsonArray(new JsonObject{["activityType"]="meal",["category"]="美食"}),["warnings"]=new JsonArray(),["finishTime"]="12:30"};
        RecommendationQuality.Assess(p,new JsonArray(day));Assert.Equal("partial",day.Str("recommendationStatus"));
    }
    [Fact] public async Task VerifiedSecondOrderCanBeatFirstFeasibleEstimate() {
        var p=Profile();p["interests"]=new JsonArray();p["start"]=new JsonObject{["name"]="起点",["lat"]=-33.86,["lng"]=151.21};p["end"]=p["start"]!.DeepClone();
        var a=new JsonObject{["id"]="a",["name"]="A",["lat"]=-33.8601,["lng"]=151.21,["visitMinutes"]=10,["openMinute"]=0,["closeMinute"]=1440,["cost"]=0};
        var b=(JsonObject)a.DeepClone();b["id"]="b";b["name"]="B";b["lat"]=-33.8602;p["mustVisit"]=new JsonArray("a","b");
        var external=new FakeExternal{Respond=call=>{
            var from=call.Payload!["origin"]!["location"]!["latLng"]!["latitude"]!.GetValue<double>();
            var to=call.Payload!["destination"]!["location"]!["latLng"]!["latitude"]!.GetValue<double>();
            return new JsonObject{["routes"]=new JsonArray(new JsonObject{["distanceMeters"]=50,["duration"]=from==a.Number("lat")&&to==b.Number("lat") ? "3600s":"60s",["polyline"]=new JsonObject{["encodedPolyline"]="test"}})};
        }};
        var result=await new RouteScheduler(new RouteService(external)).ScheduleDaysAsync(p,[a,b],new JsonObject(),new Dictionary<string,int>(),new PlanningContext("live",new Dictionary<string,string>{{"GOOGLE_SERVER_KEY","test"}}));
        Assert.Equal("b",result[0]!.Array("stops")![0]!.Str("id"));Assert.Equal(3,result[0]!.Array("legs")!.OfType<JsonObject>().Sum(l=>l.Int("minutes")));
    }
    [Fact] public async Task MixedTransportShortlistCannotRejectFarMustSeeBeforeTransitComparison() {
        var p=Profile();p["start"]=new JsonObject{["name"]="中央车站",["lat"]=-33.883,["lng"]=151.2065};p["end"]=p["start"]!.DeepClone();p["maxSteps"]=5000;p["transportPreference"]="lessWalking";p["request"]="这次轻松一点，尽量少走";p["mustVisit"]=new JsonArray("opera");
        var result=await Planner().PrepareWalkingAsync(p);
        Assert.Contains(result.Array("candidateDays")![0]!.AsArray(),id=>id!.GetValue<string>()=="opera");
    }
    Planner Planner()=>new(new PlaceService(new FakeExternal(),catalog),new RecommendationService(new FakeExternal(),new FakeMemory()),new RouteScheduler(new RouteService(new FakeExternal())),new WeatherService(new FakeExternal()),new SecretStore(new ProjectPaths(root)));
    [Fact]public async Task DirectionalRoadTimesDetermineOrderAndNeverUseStraightLineFallback(){var p=Profile();var points=catalog.Get().Take(2).Select(x=>(JsonObject)x.DeepClone()).ToArray();foreach(var x in points){x["visitMinutes"]=5;x["openMinute"]=0;x["closeMinute"]=1440;}p["mustVisit"]=new JsonArray(points.Select(x=>(JsonNode)JsonValue.Create(x.Str("id"))!).ToArray());var matrix=Matrix(points.Append(p.Object("start")!), (a,b)=>a.Str("id")==points[0].Str("id")&&b.Str("id")==points[1].Str("id")?3600:60);var context=new PlanningContext("demo",new Dictionary<string,string>()){Walking=new(matrix)};var result=await new RouteScheduler(new RouteService(new FakeExternal())).ScheduleDaysAsync(p,points,new JsonObject(),new Dictionary<string,int>(),context);Assert.Equal(points[1].Str("id"),result[0]!.Array("stops")![0]!.Str("id"));Assert.All(result[0]!.Array("legs")!.OfType<JsonObject>(),l=>Assert.Contains("Apple",l.Str("source")));}
    [Fact]public async Task MissingRoadEvidenceFailsInsteadOfSavingEstimatedPlan(){var p=Profile();p["walkingRoutes"]=new JsonArray();var e=await Assert.ThrowsAsync<ServiceException>(()=>Planner().PlanAsync(p,"demo"));Assert.Contains("缺少真实",e.Message);}
    [Fact]public void ExpiredAndDuplicateEvidenceAreRejected(){var points=catalog.Get().Take(2).ToArray();var m=Matrix(points);m[0]!["fetchedAt"]=DateTimeOffset.UtcNow.AddDays(-1).ToString("O");Assert.Throws<ServiceException>(()=>new WalkingEvidence(m));m=Matrix(points);m.Add(m[0]!.DeepClone());Assert.Throws<ServiceException>(()=>new WalkingEvidence(m));}
    [Fact]public void HistoricalReplayNeedsExplicitEvaluatorOptIn(){var points=catalog.Get().Take(2).ToArray();var replay=new JsonArray(new JsonObject{["from"]=points[0].DeepClone(),["to"]=points[1].DeepClone(),["meters"]=50,["seconds"]=60,["recordedReplay"]=true,["recordedDate"]="2026-10-23",["source"]="Valhalla / OpenStreetMap recorded replay"});Assert.Throws<ServiceException>(()=>new WalkingEvidence(replay));var accepted=new WalkingEvidence(replay,allowHistoricalReplay:true);Assert.True(accepted.IsHistoricalReplay);Assert.Contains("Valhalla",accepted.SourceSummary);}
    [Fact]public void HistoricalSameLocationIsNotMislabeledAsApple(){var points=catalog.Get().Take(2).ToArray();var replay=new WalkingEvidence(HistoricalMatrix(points),allowHistoricalReplay:true);var leg=replay.Get(points[0],points[0]);Assert.True(leg.Bool("recordedReplay"));Assert.DoesNotContain("Apple",leg.Str("source"));}
    [Fact]public async Task HistoricalRemainingReplayRequiresExplicitEvaluationOptIn(){var trip=await Planner().PlanAsync(Profile(),"demo");trip.Object("profile")!["date"]="2026-10-23";trip.Array("days")![0]!["date"]="2026-10-23";var first=trip.Array("days")![0]!.Array("stops")![0]!.AsObject();var request=new JsonObject{["day"]=0,["completedIds"]=new JsonArray(first.Str("id")),["currentMinute"]=720,["origin"]=new JsonObject{["name"]=first.Str("name"),["lat"]=first.Number("lat"),["lng"]=first.Number("lng")},["request"]="只剩两小时",["walkingRoutes"]=HistoricalMatrix(catalog.Get().Append(trip.Object("profile")!.Object("start")!))};Assert.Throws<ServiceException>(()=>RemainingJourney.Build(trip,request,new ScheduleEditorService(catalog),catalog));var fresh=(JsonObject)request.DeepClone();fresh["walkingRoutes"]=Matrix(catalog.Get().Append(trip.Object("profile")!.Object("start")!));Assert.Throws<ServiceException>(()=>RemainingJourney.Build(trip,fresh,new ScheduleEditorService(catalog),catalog,allowHistoricalWalkingReplay:true));var future=(JsonObject)trip.DeepClone();future.Array("days")![0]!["date"]="2099-01-01";Assert.Throws<ServiceException>(()=>RemainingJourney.Build(future,request,new ScheduleEditorService(catalog),catalog,allowHistoricalWalkingReplay:true));var updated=RemainingJourney.Build(trip,request,new ScheduleEditorService(catalog),catalog,allowHistoricalWalkingReplay:true);Assert.True(updated.Array("days")![0]!.Bool("executionStarted"));Assert.Equal(first.Str("id"),updated.Array("days")![0]!.Array("completedStops")![0]!.Str("id"));}
    [Fact]public async Task EditingUsesRoadTimeForAssistedAndLimitsAreBlocking(){var trip=await Planner().PlanAsync(Profile(),"demo");trip["mode"]="assisted";var points=catalog.Get().Concat(new[]{trip.Object("profile")!.Object("start")!});var roads=Matrix(points);var editor=new ScheduleEditorService(catalog);var value=editor.Build(new JsonObject{["trip"]=trip.DeepClone(),["walkingRoutes"]=roads});Assert.All(value.Array("days")![0]!.Array("legs")!.OfType<JsonObject>(),l=>Assert.Contains("Apple",l.Str("source")));var settings=(JsonObject)value.Object("schedule")!.Object("settings")!.DeepClone();settings["maxSteps"]=500;var retry=editor.Build(new JsonObject{["trip"]=value,["settings"]=settings,["days"]=value.Object("schedule")!.Array("days")!.DeepClone(),["walkingRoutes"]=roads.DeepClone()});Assert.Contains(retry.Object("schedule")!.Array("issues")!.OfType<JsonObject>(),i=>i.Str("code")=="steps"&&i.Bool("blocking"));}
    [Fact]public async Task RemainingPlanPreservesCompletedStopsOtherDatesAndOriginal(){var trip=await Planner().PlanAsync(Profile(2),"demo");var before=trip.ToJsonString();var first=trip.Array("days")![0]!.Array("stops")![0]!.AsObject();var request=new JsonObject{["day"]=0,["completedIds"]=new JsonArray(first.Str("id")),["currentMinute"]=720,["origin"]=new JsonObject{["name"]="当前位置",["lat"]=first.Number("lat"),["lng"]=first.Number("lng")},["request"]="只剩两小时",["walkingRoutes"]=Matrix(catalog.Get().Append(trip.Object("profile")!.Object("start")!))};var updated=RemainingJourney.Build(trip,request,new ScheduleEditorService(catalog),catalog);Assert.Equal(before,trip.ToJsonString());Assert.Equal(trip.Array("days")![1]!.ToJsonString(),updated.Array("days")![1]!.ToJsonString());Assert.Equal(first.Str("id"),updated.Array("days")![0]!.Array("completedStops")![0]!.Str("id"));Assert.DoesNotContain(updated.Array("days")![0]!.Array("stops")!.OfType<JsonObject>(),s=>s.Str("id")==first.Str("id"));Assert.Equal(720,updated.Array("days")![0]!.Int("planningStartMinute"));TripContractValidator.Validate(updated);foreach(var d in updated.Object("schedule")!.Array("days")!.OfType<JsonObject>()){foreach(var key in new[]{"transportMinutes","queueMinutes","timeUsed","timeRemaining","estimatedSteps","estimatedCost"})Assert.NotNull(d[key]);Assert.NotNull(d["finishTime"]);Assert.NotNull(d["unknownQueue"]);}}
    [Fact]public void AdjustmentIntentHonorsNegationAndEnglishTime(){Assert.False(TravelAdjustmentIntent.Lighter("我不累，不用少走"));Assert.True(TravelAdjustmentIntent.Lighter("I'm tired, less walking"));Assert.False(TravelAdjustmentIntent.Rain("现在不下雨"));Assert.Equal(120,TravelAdjustmentIntent.RemainingMinutes("I have 2 hours left"));Assert.Equal(120,TravelAdjustmentIntent.RemainingMinutes("只剩两小时"));}
    [Fact]public async Task NewMustVisitIsIncludedInRemainingProposal(){var trip=await Planner().PlanAsync(Profile(),"demo");var used=trip.Array("days")![0]!.Array("stops")!.OfType<JsonObject>().Select(s=>s.Str("id")).ToHashSet();var extra=catalog.Get().First(p=>!used.Contains(p.Str("id")));var request=new JsonObject{["day"]=0,["constraints"]=new JsonObject{["mustDo"]=extra.Str("name")},["walkingRoutes"]=Matrix(catalog.Get().Append(trip.Object("profile")!.Object("start")!))};var result=RemainingJourney.Build(trip,request,new ScheduleEditorService(catalog),catalog);Assert.Contains(result.Object("schedule")!.Array("days")![0]!.Array("activities")!.OfType<JsonObject>(),a=>a.Str("placeId")==extra.Str("id"));}
    [Fact]public async Task SkippedStopIsArchivedWithoutBecomingVisited(){var trip=await Planner().PlanAsync(Profile(),"demo");var stops=trip.Array("days")![0]!.Array("stops")!;var first=stops[0]!.AsObject();var request=new JsonObject{["day"]=0,["skippedIds"]=new JsonArray(first.Str("id")),["currentMinute"]=720,["origin"]=trip.Object("profile")!["start"]!.DeepClone(),["walkingRoutes"]=Matrix(catalog.Get().Append(trip.Object("profile")!.Object("start")!))};var result=RemainingJourney.Build(trip,request,new ScheduleEditorService(catalog),catalog);Assert.Empty(result.Array("days")![0]!.Array("completedStops")!);Assert.Equal(first.Str("id"),result.Array("days")![0]!.Array("skippedStops")![0]!.Str("id"));}
    [Fact]public async Task CustomDepartureTimeTravelsWithEveryDayForNativeMapPreview(){var p=Profile(2);p["startMinute"]=780;var trip=await Planner().PlanAsync(p,"demo");Assert.All(trip.Array("days")!.OfType<JsonObject>(),d=>Assert.Equal(780,d.Int("planningStartMinute")));}
    [Fact]public async Task FatigueLimitOnlyAppliesToSelectedDay(){var trip=await Planner().PlanAsync(Profile(2),"demo");var request=new JsonObject{["day"]=0,["request"]="轻松一点，明天不要改",["walkingRoutes"]=Matrix(catalog.Get().Append(trip.Object("profile")!.Object("start")!))};var result=RemainingJourney.Build(trip,request,new ScheduleEditorService(catalog),catalog);Assert.Equal(7000,result.Object("schedule")!.Array("days")![0]!.Int("maxSteps"));Assert.Equal(trip.Object("profile")!.Int("maxSteps"),result.Object("schedule")!.Object("settings")!.Int("maxSteps"));Assert.Equal(trip.Array("days")![1]!.ToJsonString(),result.Array("days")![1]!.ToJsonString());}
    [Fact] public async Task ThreeDayRainyFoodTripHasDistinctRealLunchesAndNearbyVisits() {
        var p=Profile(3);p["interests"]=new JsonArray("美食");p["maxSteps"]=5000;p["energy"]="低精力";p["rhythm"]="轻松随意";p["budget"]=120;p["weatherOverride"]="rain";
        p["walkingRoutes"]=Matrix(catalog.Get().Append(p.Object("start")!));
        var trip=await Planner().PlanAsync(p,"demo");var all=trip.Array("days")!.OfType<JsonObject>().ToArray();
        Assert.Equal("ready",trip.Str("recommendationStatus"));
        Assert.All(all,d=> {
            var stops=d.Array("stops")!.OfType<JsonObject>().ToArray();Assert.InRange(stops.Length,2,3);
            var meal=Assert.Single(stops,RecommendationQuality.IsMeal);Assert.StartsWith("https://",meal.Str("sourceURL"));
            Assert.InRange(string.CompareOrdinal(meal.Str("arrival"),"11:30"),0,int.MaxValue);
            Assert.InRange(string.CompareOrdinal(meal.Str("departure"),"14:30"),int.MinValue,0);
            Assert.Contains(stops,s=>!RecommendationQuality.IsMeal(s));Assert.InRange(d.Int("estimatedSteps"),0,5000);
        });
        Assert.Contains(all,d=>d.Array("stops")!.Count==3);
        var ids=all.SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).Select(s=>s.Str("id")).ToArray();Assert.Equal(ids.Length,ids.Distinct().Count());
    }
    [Fact] public async Task FoodCannotBeClaimedSatisfiedByAnAreaOrAnUnaffordableRestaurant() {
        Assert.False(RecommendationQuality.Matches(catalog.Get().Single(p=>p.Str("id")=="rocks"),"美食"));
        var p=Profile();p["budget"]=0;p["interests"]=new JsonArray("美食");
        var trip=await Planner().PlanAsync(p,"demo");Assert.Equal("partial",trip.Str("recommendationStatus"));
        Assert.Contains(trip.Array("warnings")!,w=>w!.GetValue<string>().Contains("未满足「美食」"));
        Assert.Contains("待补充草案",trip.Str("explanation"));
    }
    [Fact] public async Task PreparationKeepsDailySearchBoundedAndRoadGroupsAreValidated() {
        var p=Profile(3);p["energy"]="低精力";p["interests"]=new JsonArray("美食");
        var shortlist=await Planner().PrepareWalkingAsync(p);var groups=shortlist.Array("candidateDays")!;
        Assert.Equal(3,groups.Count);Assert.All(groups,g=>Assert.InRange(g!.AsArray().Count,2,4));
        p["walkingDayCandidateIds"]=new JsonArray(new JsonArray("unknown"));p["walkingRoutes"]=Matrix(catalog.Get().Append(p.Object("start")!));
        await Assert.ThrowsAsync<ServiceException>(()=>Planner().PlanAsync(p,"demo"));
    }
    [Fact] public async Task RoadVerifiedRestaurantStaysAMealDuringEditing() {
        var p=Profile();p["interests"]=new JsonArray("美食");p["energy"]="低精力";p["maxSteps"]=5000;p["walkingRoutes"]=Matrix(catalog.Get().Append(p.Object("start")!));
        var trip=await Planner().PlanAsync(p,"demo");var planDay=trip.Array("days")![0]!.AsObject();
        var input=new JsonObject{["date"]=planDay.Str("date"),["startMinute"]=p.Int("startMinute"),["endMinute"]=p.Int("endMinute"),["activities"]=new JsonArray(planDay.Array("stops")!.OfType<JsonObject>().Select(s=>(JsonNode)new JsonObject{["id"]=s.Str("id"),["placeId"]=s.Str("id"),["kind"]=RecommendationQuality.IsMeal(s)?"meal":"attraction",["duration"]=s.Int("visitMinutes")}).ToArray())};
        var edited=new ScheduleEditorService(catalog).Build(new JsonObject{["trip"]=trip,["days"]=new JsonArray(input),["walkingRoutes"]=p["walkingRoutes"]!.DeepClone()});
        Assert.DoesNotContain(edited.Object("schedule")!.Array("issues")!.OfType<JsonObject>(),i=>i.Bool("blocking"));
        var meal=Assert.Single(edited.Array("days")![0]!.Array("stops")!.OfType<JsonObject>(),RecommendationQuality.IsMeal);Assert.Equal("美食",meal.Str("category"));Assert.DoesNotContain("佔位",meal.Str("note"));Assert.InRange(edited.Array("days")![0]!.Int("estimatedSteps"),0,5000);
    }
    [Fact] public async Task GeneralFoodWishBecomesTripInterestRatherThanAnUnknownMandatoryPlace() {
        var p=Profile();p["interests"]=new JsonArray("文化");p["mustDo"]="想吃当地美食";
        var trip=await Planner().PlanAsync(p,"demo");Assert.Contains(trip.Object("profile")!.Array("interests")!,x=>x!.GetValue<string>()=="美食");
        Assert.Contains(trip.Array("days")![0]!.Array("stops")!.OfType<JsonObject>(),RecommendationQuality.IsMeal);
    }
    [Fact] public async Task ShortMorningFoodTripUsesBreakfastInsteadOfBlockingAllRestaurants() {
        var p=Profile();p["startMinute"]=540;p["endMinute"]=690;p["interests"]=new JsonArray("美食");
        var trip=await Planner().PlanAsync(p,"demo");Assert.Contains(trip.Array("days")![0]!.Array("stops")!.OfType<JsonObject>(),RecommendationQuality.IsMeal);
    }
    [Fact] public async Task CoreInterestWinsOverAnUnrelatedExtraActivityAndMeal() {
        var p=Profile();p["maxSteps"]=6500;p["budget"]=120;p["weatherOverride"]="rain";p["energy"]="低精力";p["interests"]=new JsonArray("自然");
        var trip=await Planner().PlanAsync(p,"demo");
        Assert.Contains(trip.Array("days")![0]!.Array("stops")!.OfType<JsonObject>(),s=>RecommendationQuality.Matches(s,"自然"));
    }
    public void Dispose()=>Directory.Delete(root,true);
}
