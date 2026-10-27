using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;

public sealed class WalkingLoadPacingTests
{
    static JsonObject Profile()=>PlanValidator.Validate(new JsonObject{["date"]="2026-10-28",["budget"]=500,["maxSteps"]=60000},[]);
    static JsonObject Place(string id,int open=600,int close=1440,int duration=75)=>new(){["id"]=id,["name"]=id,["lat"]=-33.86111,["lng"]=151.21261,["visitMinutes"]=duration,["cost"]=0,["note"]="Test",["indoor"]=true,["category"]="文化",["openMinute"]=open,["closeMinute"]=close};
    [Fact]public void ContinuousWalkingAndSeatedMealsAreNotTheSameEstimate(){
        var p=Profile();var museum=Place("museum",duration:60);
        Assert.Equal(100,WalkingLoad.Cadence(p));Assert.Equal(1800,WalkingLoad.VisitSteps(museum,60,p));
        museum["walkingFraction"]=1;Assert.Equal(6000,WalkingLoad.VisitSteps(museum,60,p));
        museum["activityType"]="meal";Assert.Equal(0,WalkingLoad.VisitSteps(museum,60,p));
        Assert.Equal(1000,WalkingLoad.DistanceSteps(750,p));
        p["walkingSpeedKmh"]=3;Assert.InRange(WalkingLoad.VisitSteps(Place("gallery",duration:60),60,p),1199,1201);
    }
    [Fact]public void DefaultsAndConfirmedHotelNeverInventABooking(){
        var p=Profile();Assert.Equal(1200,p.Int("endMinute"));Assert.Equal(1200,p.Int("targetReturnMinute"));
        p["hotel"]=new JsonObject{["name"]="Selected hotel",["lat"]=-33.86,["lng"]=151.21};p["accommodationStatus"]="selected";p.Remove("end");
        var validated=PlanValidator.Validate(p,[]);Assert.Equal("Selected hotel",validated.Object("end")!.Str("name"));
        p["accommodationStatus"]="unknown";Assert.Throws<ServiceException>(()=>PlanValidator.Validate(p,[]));
    }
    [Theory][InlineData("walkingSpeedKmh",0)][InlineData("walkingSpeedKmh",8)][InlineData("stepLengthMeters",0)][InlineData("stepLengthMeters",2)]
    public void InvalidWalkingParametersAreRejected(string key,double value){var p=Profile();p[key]=value;Assert.Throws<ServiceException>(()=>PlanValidator.Validate(p,[]));}
    [Fact]public async Task FullDayCanReachEveningWithLunchDinnerAndHotelReturn(){
        var p=Profile();p["hotel"]=p["end"]!.DeepClone();p["accommodationStatus"]="selected";
        var places=Enumerable.Range(0,6).Select(i=>Place("museum-"+i,close:1020)).ToList();
        places.Add(Place("evening",1020,1200,60));
        var lunch=Place("lunch",690,870,45);lunch["activityType"]="meal";places.Add(lunch);
        var dinner=Place("dinner",1020,1170,45);dinner["activityType"]="meal";places.Add(dinner);
        var result=await new RouteScheduler(new RouteService(new FakeExternal())).ScheduleDaysAsync(p,places,new(),new Dictionary<string,int>(),new("demo",new Dictionary<string,string>()));
        var day=result[0]!.AsObject();var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();
        Assert.True(string.CompareOrdinal(day.Str("finishTime"),"19:00")>=0,day.ToJsonString());Assert.True(string.CompareOrdinal(day.Str("finishTime"),"20:00")<=0);
        Assert.Contains(stops,s=>s.Str("id")=="dinner");Assert.Equal(stops.Length+1,day.Array("legs")!.Count);
        Assert.True(stops.Length>4); // New full-day drafts must also remain saveable.
        TripContractValidator.Validate(new JsonObject{["title"]="Test",["mode"]="demo",["createdAt"]="2026-10-27T00:00:00Z",["explanation"]="Test",["profile"]=p.DeepClone(),["days"]=result.DeepClone(),["weather"]=new JsonObject{["summary"]="Test",["source"]="Test",["rainy"]=false},["warnings"]=new JsonArray(),["trace"]=new JsonArray()});
        Assert.Equal("selected_unbooked_hotel",day.Str("returnLocationStatus"));Assert.Equal(day.Int("estimatedSteps"),day.Object("stepEstimate")!.Int("routeSteps")+day.Object("stepEstimate")!.Int("activitySteps"));
    }
    [Fact]public async Task FatigueReducesOptionalLoadWithoutRaisingStepCap(){
        var p=Profile();p["energy"]="低精力";var places=Enumerable.Range(0,9).Select(i=>Place("p"+i,duration:50)).ToArray();
        var scheduler=new RouteScheduler(new RouteService(new FakeExternal()));
        var high=(JsonObject)p.DeepClone();high["energyFeedback"]="energized";
        var tired=(JsonObject)p.DeepClone();tired["energyFeedback"]="tired";
        var a=await scheduler.ScheduleDaysAsync(high,places,new(),new Dictionary<string,int>(),new("demo",new Dictionary<string,string>()));
        var b=await scheduler.ScheduleDaysAsync(tired,places,new(),new Dictionary<string,int>(),new("demo",new Dictionary<string,string>()));
        Assert.True(a[0]!.Array("stops")!.Count>b[0]!.Array("stops")!.Count);Assert.Equal(60000,high.Int("maxSteps"));
        Assert.Equal("early_for_energy",b[0]!.Str("returnTargetStatus"));
    }
    [Fact]public async Task HardClosingTimeAndEarlierUserEndStillWin(){
        var p=Profile();p["endMinute"]=900;p["targetReturnMinute"]=900;
        var result=await new RouteScheduler(new RouteService(new FakeExternal())).ScheduleDaysAsync(p,[Place("short",close:800)],new(),new Dictionary<string,int>(),new("demo",new Dictionary<string,string>()));
        Assert.True(string.CompareOrdinal(result[0]!.Str("finishTime"),"15:00")<=0);Assert.Equal("15:00",result[0]!.Str("targetReturnTime"));
    }
    [Fact]public void FeedbackRespectsNegation(){Assert.True(TravelAdjustmentIntent.Lighter("有點累，想少走"));Assert.False(TravelAdjustmentIntent.Lighter("我不累，不用少走"));Assert.True(TravelAdjustmentIntent.MoreEnergy("還有體力，多排一點"));Assert.False(TravelAdjustmentIntent.MoreEnergy("不要增加行程"));}
    [Fact]public void BothSkillsRetainExistingConstraintsAndNewWalkingReturnRules(){
        foreach(var language in new[]{"en","zh-Hant"}){
            var skill=ItinerarySkill.Read(language);Assert.Contains("preferredIds",skill);Assert.Contains("accommodationIds",skill);Assert.Contains("outstandingChecks",skill);
            Assert.Contains("2026-10-27.2",skill);Assert.Contains("20:00",skill);Assert.Contains("0.75",skill);Assert.Contains("4.5",skill);
            Assert.Contains(language=="en"?"## 10. Output requirements":"## 十、輸出要求",skill);
            Assert.Contains(language=="en"?"Never silently omit a must-visit item":"不得默默省略必去項目",skill);
        }
    }
    [Fact]public async Task WalkingRefreshUsesSharedModelForMealsAndOnSiteSteps(){
        var p=Profile();var meal=Place("meal",690,870,45);meal["activityType"]="meal";meal["category"]="美食";
        var gallery=Place("gallery",duration:30);p["mustVisit"]=new JsonArray("meal","gallery");
        var scheduler=new RouteScheduler(new RouteService(new FakeExternal()));
        var days=await scheduler.ScheduleDaysAsync(p,[meal,gallery],new(),new Dictionary<string,int>(),new("demo",new Dictionary<string,string>()));
        var trip=new JsonObject{["title"]="Test",["mode"]="demo",["profile"]=p.DeepClone(),["days"]=days,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["weather"]=new JsonObject{["summary"]="Test",["source"]="Test",["rainy"]=false},["warnings"]=new JsonArray(),["trace"]=new JsonArray(),["explanation"]="Test"};
        var roads=new JsonArray(days[0]!.Array("legs")!.Select(_=>(JsonNode)new JsonObject{["meters"]=75,["seconds"]=60}).ToArray());
        var refreshed=WalkingTimeUpdate.Apply(trip,0,roads,[meal,gallery]);var day=refreshed.Array("days")![0]!.AsObject();
        Assert.Equal(1200,day.Int("estimatedSteps")); // 3 × 100 road steps + 30 × .3 × 100 on site.
        Assert.Equal(0,day.Array("stops")!.OfType<JsonObject>().Single(s=>s.Str("id")=="meal").Int("activitySteps"));
        Assert.Equal(1200,day.Array("stops")!.OfType<JsonObject>().Sum(s=>s.Int("steps"))+100);
    }

    [Fact]public void HotelCannotSilentlyReplaceAnExplicitAirportEndpoint(){
        var p=Profile();p["hotel"]=new JsonObject{["name"]="Hotel",["lat"]=-33.86,["lng"]=151.21};p["accommodationStatus"]="booked";
        p["end"]=new JsonObject{["name"]="Airport",["lat"]=-33.9357,["lng"]=151.166};
        var result=PlanValidator.Validate(p,[]);Assert.Equal("Airport",result.Object("end")!.Str("name"));Assert.Equal("Hotel",result.Object("hotel")!.Str("name"));
    }

}
