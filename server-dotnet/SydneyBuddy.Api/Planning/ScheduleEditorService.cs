using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// <summary>Local, deterministic schedule editor. Never saves or invokes external providers.</summary>
public sealed class ScheduleEditorService(PlaceCatalog catalog,RestaurantStore? restaurants=null)
{
    static JsonObject Copy(JsonObject x)=>(JsonObject)x.DeepClone();
    static string Time(int m)=>$"{m/60:00}:{m%60:00}";
    static int N(JsonObject x,string key,int fallback,int min,int max) {
        var n=x.Number(key,double.NaN);
        if(x[key] is null)n=fallback;
        if(!double.IsFinite(n)||n!=Math.Truncate(n)||n<min||n>max)throw new ServiceException(key+" Invalid value.");
        return (int)n;
    }
    static double Cost(JsonObject x,string key,double fallback=0) {
        var n=x[key] is null?fallback:x.Number(key,double.NaN);
        if(!double.IsFinite(n)||n<0||n>100000)throw new ServiceException(key+" Invalid amount.");
        return Math.Round(n,2,MidpointRounding.AwayFromZero);
    }
    static JsonObject Point(JsonObject x)=>new(){["name"]=x.Str("name"),["lat"]=x.Number("lat"),["lng"]=x.Number("lng")};
    static void CheckPoint(JsonObject x) {
        var lat=x.Number("lat",double.NaN);var lng=x.Number("lng",double.NaN);
        if(!double.IsFinite(lat)||!double.IsFinite(lng)||lat is < -34.2 or > -33.5||lng is <150.5 or >151.5||x.Str("name").Length>120)throw new ServiceException("Enter a valid place and coordinates within Sydney.");
    }
    public static JsonObject Attraction(JsonObject p,string pace)=>new(){["id"]=Guid.NewGuid().ToString("N"),["placeId"]=p.Str("id"),["kind"]=PoiService.IsImported(p)?PoiService.ActivityKind(p):RecommendationQuality.IsMeal(p)?"meal":"attraction",["name"]=p.Str("name"),["lat"]=p.Number("lat"),["lng"]=p.Number("lng"),["cost"]=p.Number("cost"),["openMinute"]=p.Int("openMinute"),["closeMinute"]=p.Int("closeMinute"),["duration"]=Math.Clamp(p.Int("visitMinutes")+(pace=="relaxed"?15:pace=="packed"?-10:0),15,180),["queueMinutes"]=null,["locked"]=false};
    static JsonObject Facility(string kind,string name,JsonObject p,int duration,double cost=0,int open=0,int close=1440)=>new(){["id"]=Guid.NewGuid().ToString("N"),["kind"]=kind,["name"]=name,["lat"]=p.Number("lat"),["lng"]=p.Number("lng"),["duration"]=duration,["cost"]=cost,["confirmed"]=false,["openMinute"]=open,["closeMinute"]=close,["locked"]=false};
    static JsonObject Radar(string name,double value)=>new(){["name"]=name,["value"]=Math.Clamp(value,0,1)};
    public static string KindLabel(string kind)=>kind switch {"meal"=>"Meal","restroom"=>"Restroom","rest"=>"Rest","stay"=>"Check-in","checkout"=>"Check-out","bag-drop"=>"Luggage drop-off","bag-pickup"=>"Luggage pickup",_=>"Attraction"};

    public JsonObject Build(JsonObject request,bool allowHistoricalTransit=false) {
        var trip=Copy(request.Object("trip")??throw new ServiceException("Trip is missing."));
        if(trip.Str("mode")=="live")throw new ServiceException("Use a matching route service to edit trips based on Google data.");
        var evidence=request.Array("walkingRoutes")??trip.Array("walkingRoutes");
        var walking=evidence is null ? null:new WalkingEvidence(evidence);
        if(trip.Str("mode")!="demo" && walking is null)throw new ServiceException("Editing an online trip requires actual walking data.");
        if(evidence is not null)trip["walkingRoutes"]=evidence.DeepClone();
        var transportInput=request.Array("transportRoutes")??trip.Array("transportRoutes");
        var transit=new TransportEvidence(transportInput,allowHistoricalTransit || request.Array("transportRoutes") is null);
        if(transportInput is not null)trip["transportRoutes"]=transportInput.DeepClone();
        var previousDays=(JsonArray?)trip.Array("days")?.DeepClone();
        var places=PlanningCandidates.Merge(catalog.Get(),trip.Array("planningCandidates")).ToList();
        var imported=request.Array("candidates")??trip.Object("schedule")?.Array("candidates");
        if(imported is not null) {
            if(imported.Count>150)throw new ServiceException("The toolbox supports up to 150 places.");
            foreach(var p in imported.OfType<JsonObject>().Where(PoiService.IsImported)) {
                var authoritative=p.Str("id").StartsWith("restaurant:") ? restaurants?.Get(p.Str("id"))??throw new ServiceException("Restaurant data was not found. Please search again.") : p;
                var normalized=PoiService.Import(authoritative);if(places.All(x=>x.Str("id")!=normalized.Str("id")))places.Add(normalized);
            }
        }
        // The legacy Rocks estimate includes food; meals have their own allowance here.
        foreach(var place in places.Where(p=>p.Str("id")=="rocks")) {place["cost"]=0;place["note"]="Neighborhood visits exclude meals. Meal stops are budgeted separately to avoid reserving the same cost twice.";}
        var profile=PlanValidator.Validate(trip.Object("profile")??throw new ServiceException("Preferences are missing."),places);
        profile["request"]=profile.Str("request");
        var settings=Copy(request.Object("settings")??trip.Object("schedule")?.Object("settings")??new());
        var pace=settings.Str("pace","balanced");if(pace is not("relaxed" or "balanced" or "packed"))throw new ServiceException("Invalid trip pace.");
        settings["pace"]=pace;settings["timeLimit"]=N(settings,"timeLimit",180,0,1440);settings["restroomInterval"]=N(settings,"restroomInterval",120,30,360);
        settings["mealCost"]=Cost(settings,"mealCost",25);settings["dailyBudget"]=Cost(settings,"dailyBudget",profile.Number("budget"));
        settings["totalBudget"]=Cost(settings,"totalBudget",profile.Number("budget")*profile.Int("days"));settings["maxSteps"]=N(settings,"maxSteps",profile.Int("maxSteps"),500,60000);
        settings["luggage"]=settings.Bool("luggage");
        var rainy=request.Bool("rainy",trip.Object("weather")?.Bool("rainy")??false);
        var must=profile.Array("mustVisit")!.Select(x=>x!.GetValue<string>()).ToHashSet();
        var interests=profile.Array("interests")!.Select(x=>x!.GetValue<string>()).ToHashSet();
        var ranked=places.OrderByDescending(x=>(must.Contains(x.Str("id"))?100:0)+(interests.Contains(x.Str("category"))?20:0)+(rainy?(x.Bool("indoor")?15:-20):0)).ThenBy(x=>RouteService.Distance(profile.Object("start")!,x)).ToArray();
        var candidates=new JsonArray();foreach(var place in ranked){var p=Copy(place);p["reason"]=(interests.Contains(p.Str("category"))?"Matches your interests; ":"Nearby candidate along the route; ")+(p["indoorKnown"] is not null&&!p.Bool("indoorKnown")?"Indoor/outdoor status unverified":p.Bool("indoor")?"Indoor activity":rainy?"Outdoor; take care in rain":"Outdoor activity");candidates.Add(p);}
        var inputs=request.Array("days");var count=profile.Int("days");if(inputs is not null&&inputs.Count!=count)throw new ServiceException("The number of days is inconsistent.");
        var days=new JsonArray();var legacy=new JsonArray();var issues=new JsonArray();var selected=new HashSet<string>();var autoUsed=new HashSet<string>();double totalCost=0;
        var date0=DateOnly.ParseExact(profile.Str("date"),"yyyy-MM-dd",CultureInfo.InvariantCulture);var previousStay=Point(profile.Object("start")!);
        for(var d=0;d<count;d++){
            var date=date0.AddDays(d).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);var input=inputs?[d] as JsonObject;
            var stay=Copy(input?.Object("stay")??new JsonObject{["name"]="Accommodation to confirm",["lat"]=profile.Object("end")!.Number("lat"),["lng"]=profile.Object("end")!.Number("lng"),["cost"]=0,["confirmed"]=false});
            CheckPoint(stay);stay["cost"]=Cost(stay,"cost");stay["checkIn"]=N(stay,"checkIn",900,0,1439);stay["checkOut"]=N(stay,"checkOut",660,0,1439);
            var start=(previousDays?[d]?.Bool("executionStarted")==true || input?.Bool("startOverride")==true) && input?.Object("start") is {} requestedStart ? Point(requestedStart):d==0?Point(profile.Object("start")!):Point(previousStay);CheckPoint(start);var end=input?.Bool("endOverride")==true && input.Object("end") is {} requestedEnd ? Point(requestedEnd):d==count-1?Point(profile.Object("end")!):Point(stay);CheckPoint(end);
            var dailySteps=N(input??new JsonObject(),"maxSteps",settings.Int("maxSteps"),500,60000);var dailyBudget=Cost(input??new JsonObject(),"dailyBudget",settings.Number("dailyBudget"));
            var startM=N(input??profile,"startMinute",profile.Int("startMinute"),0,1439);var endM=N(input??profile,"endMinute",profile.Int("endMinute"),startM+1,1440);
            JsonArray activities;
            if(input is not null)activities=(JsonArray)(input.Array("activities")??throw new ServiceException("Activity list is missing.")).DeepClone();
            else {
                activities=new();if(settings.Bool("luggage"))activities.Add(Facility("bag-drop","Luggage drop-off · to confirm",start,10));
                var picks=ranked.Where(p=>!RecommendationQuality.IsMeal(p)&&!autoUsed.Contains(p.Str("id"))&&(!rainy||p.Bool("indoor")||must.Contains(p.Str("id")))).Take(pace=="relaxed"?1:pace=="packed"?3:2).ToList();
                if(d==count-1)picks.AddRange(ranked.Where(p=>must.Contains(p.Str("id"))&&!autoUsed.Contains(p.Str("id"))&&!picks.Contains(p)));
                var anchor=start;
                foreach(var p in picks){
                    if(activities.OfType<JsonObject>().Count(a=>a.Str("kind")=="attraction")==1&&startM<840&&endM>780)activities.Add(Facility("meal","Lunch · choose a restaurant",anchor,45,settings.Number("mealCost"),720,840));
                    activities.Add(Attraction(p,pace));autoUsed.Add(p.Str("id"));anchor=p;
                }
                if(!activities.OfType<JsonObject>().Any(a=>a.Str("kind")=="meal")&&startM<840&&endM>780)activities.Add(Facility("meal","Lunch · choose a restaurant",anchor,45,settings.Number("mealCost"),720,840));
                if(endM>=1140)activities.Add(Facility("meal","Dinner · choose a restaurant",anchor,45,settings.Number("mealCost"),1020,1200));
                if(settings.Bool("luggage"))activities.Add(Facility("bag-pickup","Luggage pickup · to confirm",start,10));
                if(d<count-1)activities.Add(Facility("stay","Check-in · "+stay.Str("name"),stay,15,0,stay.Int("checkIn"),1380));
                if(d>0 && (d==count-1 || RouteService.Distance(previousStay,stay)>20))activities.Insert(0,Facility("checkout","Check-out · "+previousStay.Str("name"),previousStay,10,0,0,previousStay.Int("checkOut",660)));
                activities.Insert(Math.Min(2,activities.Count),Facility("restroom","Restroom stop · facilities to confirm",picks.FirstOrDefault()??start,10));
                if(pace=="relaxed")activities.Insert(Math.Min(4,activities.Count),Facility("rest","Rest / free time",anchor,20));
            }
            if(inputs is null) {
                // Schedule restroom opportunities throughout the day, including long waits before meals.
                var expanded=new JsonArray();var approximateClock=startM;var last=startM;var anchor=start;
                foreach(var original in activities.OfType<JsonObject>()) {
                    var a=Copy(original);var point=a.Str("kind")=="attraction" ? places.First(p=>p.Str("id")==a.Str("placeId")) : a;
                    var begin=approximateClock+(int)RouteService.EstimateLeg(anchor,point).Number("minutes");
                    var open=a.Str("kind")=="attraction" ? point.Int("openMinute") : a.Int("openMinute");begin=Math.Max(begin,open);
                    var attempts=0;
                    while(a.Str("kind")!="restroom" && begin+a.Int("duration")-last>settings.Int("restroomInterval") && attempts++<8) {
                        var bath=Facility("restroom","Restroom stop · facilities to confirm",anchor,10);
                        var at=Math.Max(approximateClock,Math.Min(begin,last+settings.Int("restroomInterval")-10));
                        bath["fixedStart"]=at;expanded.Add(bath);last=at+10;approximateClock=last;
                        begin=Math.Max(approximateClock+(int)RouteService.EstimateLeg(anchor,point).Number("minutes"),open);
                        if(begin+a.Int("duration")-last>settings.Int("restroomInterval") && begin<=approximateClock)break;
                    }
                    expanded.Add(a);approximateClock=begin+a.Int("duration");anchor=point;if(a.Str("kind")=="restroom")last=approximateClock;
                }
                if(approximateClock+(int)RouteService.EstimateLeg(anchor,end).Number("minutes")-last>settings.Int("restroomInterval"))expanded.Add(Facility("restroom","Restroom before return · facilities to confirm",anchor,10));
                activities=expanded;
            }
            // Stay changes must update dependent arrival/departure task locations as well.
            foreach(var a in activities.OfType<JsonObject>()) {
                if(a.Str("kind")=="stay") {a["name"]="Check-in · "+stay.Str("name");a["lat"]=stay.Number("lat");a["lng"]=stay.Number("lng");a["openMinute"]=stay.Int("checkIn");}
                if(a.Str("kind")=="checkout" && d>0) {a["lat"]=previousStay.Number("lat");a["lng"]=previousStay.Number("lng");a["closeMinute"]=previousStay.Int("checkOut",660);}
            }
            if(activities.Count>30||activities.Any(a=>a is not JsonObject))throw new ServiceException("Each day supports up to 30 valid activities.");
            var history=previousDays?[d]?.Array("completedStops")?.OfType<JsonObject>().ToArray()??[];
            var ids=new HashSet<string>();var stops=new JsonArray();var legs=new JsonArray();var clock=startM;var travel=0;var queueTotal=0;var unknown=false;var steps=history.Sum(s=>s.Int("steps"));var previous=start;var lastBathroom=startM;var bag=false;JsonObject? bagPoint=null;var facilityUnknown=false;
            long cents=(d<count-1?(long)Math.Round(stay.Number("cost")*100):0)+(long)Math.Round(history.Sum(s=>s.Number("cost"))*100);
            void Issue(string code,string text,string? id=null,bool blocking=false)=>issues.Add(new JsonObject{["code"]=code,["message"]=$"Day {d+1}: {text}",["day"]=d,["activityId"]=id,["blocking"]=blocking});
            foreach(var a in activities.OfType<JsonObject>()){
                var id=a.Str("id");if(id.Length is <1 or >120||!ids.Add(id))throw new ServiceException("Activity ID is invalid or duplicated.");
                var kind=a.Str("kind");if(kind is not("attraction" or "meal" or "restroom" or "rest" or "bag-drop" or "bag-pickup" or "stay" or "checkout"))throw new ServiceException("Invalid activity type.");
                JsonObject point;
                if(kind=="attraction"||(kind=="meal"&&places.Any(p=>p.Str("id")==a.Str("placeId")))||(a.Str("placeId").StartsWith("osm:")||a.Str("placeId").StartsWith("apple:")||a.Str("placeId").StartsWith("overture:")||a.Str("placeId").StartsWith("restaurant:")||a.Str("placeId").StartsWith("social:")||a.Str("placeId").StartsWith("gem:"))){
                    point=places.FirstOrDefault(p=>p.Str("id")==a.Str("placeId"))??throw new ServiceException("The place is not in the catalog.");
                    if(!selected.Add(point.Str("id")))throw new ServiceException("The same place cannot be scheduled on multiple days.");
                    if(PoiService.IsImported(point)) {if(kind!=PoiService.ActivityKind(point))throw new ServiceException("The place and activity types do not match.");Issue("poi-unknown",point.Str("name")+(point.Str("id").StartsWith("restaurant:")?": opening hours need checking; meal costs use selected-menu estimates or your allowance.":kind!="attraction"&&a.Number("cost")>0?": the cost is your allowance; opening hours are unknown.":": cost and opening hours are unknown. This place's cost is excluded from the budget."),id);}
                    a["name"]=point.Str("name");a["lat"]=point.Number("lat");a["lng"]=point.Number("lng");a["cost"]=PoiService.IsImported(point)&&kind!="attraction"?Cost(a,"cost"):point.Number("cost");a["indoor"]=point.Bool("indoor");
                    if(point.Str("id").StartsWith("restaurant:")&&kind=="meal"){
                        var estimate=point.Object("restaurantData")?.Object("budgetEstimate");
                        if(a.Str("restaurantEstimateId").Length>0){estimate=restaurants?.GetEstimate(a.Str("restaurantEstimateId"))??throw new ServiceException("The meal estimate no longer exists. Please select your meal again.");if(estimate.Str("placeId")!=point.Str("id"))throw new ServiceException("The meal estimate belongs to a different branch.");}
                        if(estimate is not null && estimate.Str("status") is "ready" or "stale" && estimate.Str("currency")=="AUD"&&estimate.Str("channel")=="dine_in"){
                            var valid=RestaurantService.Fresh(estimate);
                            a["cost"]=estimate.Number("perPerson");a["restaurantEstimateId"]=estimate.Str("id");a["costBasis"]=valid?"selected_menu_per_person":"stale_estimate";var snapshot=estimate.DeepClone().AsObject();snapshot["status"]=valid?"ready":"stale";a["priceEstimate"]=snapshot;
                            if(!valid)Issue("restaurant-price-expired","Restaurant prices have expired. The historical estimate is kept and marked as expired. Please select your meal again.",id);
                        }
                    }
                }else{point=a;CheckPoint(point);facilityUnknown|=!a.Bool("confirmed");}
                var duration=N(a,"duration",30,5,240);var cost=Cost(a,"cost");var leg=transit.Get(previous,point,date,clock)??walking?.Get(previous,point)??RouteService.EstimateLeg(previous,point);if(walking is null)leg["source"]="Sample distance estimate, not road navigation";
                if(leg.Bool("unreachable"))throw new ServiceException("Cannot walk to "+a.Str("name")+". Please choose another place.");
                var minutes=(int)leg.Number("minutes");var arrival=clock+minutes+(walking is null?0:10);var fixedStart=a["fixedStart"] is null?(int?)null:N(a,"fixedStart",0,0,1439);
                if(fixedStart.HasValue&&arrival>fixedStart.Value)Issue("appointment",a.Str("name")+": cannot arrive in time for the fixed slot.",id,true);
                if(a.Bool("locked")&&a["fixedDuration"] is not null&&N(a,"fixedDuration",duration,5,240)!=duration)Issue("locked",a.Str("name")+": the fixed visit duration has changed.",id,true);
                var begin=Math.Max(arrival,fixedStart??arrival);var queue=0;
                if(kind=="attraction" || (kind=="meal" && (RecommendationQuality.IsMeal(point)||PoiService.IsImported(point)))){
                    if(a["queueMinutes"] is null)unknown=true;else queue=N(a,"queueMinutes",0,0,360);
                    var windows=RouteScheduler.OpeningWindows(point,date,out var verified);
                    if(kind=="meal" && RecommendationQuality.MealWindow(new JsonObject{["startMinute"]=startM,["endMinute"]=endM}) is {} meal)windows=windows.Select(w=>(Open:Math.Max(w.Open,meal.Open),Close:Math.Min(w.Close,meal.Close))).ToArray();
                    if(PoiService.IsImported(point)) {windows=new[]{(Open:0,Close:1440)};verified=false;}var fits=windows.Where(w=>Math.Max(begin,w.Open)+queue+duration<=w.Close).OrderBy(w=>w.Open).ToArray();
                    if(fits.Length==0)Issue("hours",a.Str("name")+": waiting and visiting cannot both fit within opening hours.",id,true);
                    else if(fixedStart.HasValue&&begin<fits[0].Open)Issue("hours",a.Str("name")+": not open at the selected time.",id,true);else begin=Math.Max(begin,fits[0].Open);
                    a["hoursVerified"]=verified;if(rainy&&!point.Bool("indoor")&&(point["indoorKnown"] is null||point.Bool("indoorKnown")))Issue("weather",a.Str("name")+" is outdoors with a risk of rain. Check the weather before departure.",id);
                }else{
                    var open=N(a,"openMinute",0,0,1440);var close=N(a,"closeMinute",1440,open,1440);
                    if(!fixedStart.HasValue)begin=Math.Max(begin,open);
                    if(begin<open||begin+duration>close)Issue("window",a.Str("name")+": outside the service or meal window.",id,true);
                }
                if(begin+queue-lastBathroom>settings.Int("restroomInterval"))Issue("restroom-gap","The gap between restroom stops exceeds your preference. Move or add a stop.",id);
                if(kind=="restroom")lastBathroom=begin+duration;
                if(kind=="bag-drop"){if(bag)Issue("luggage","Duplicate luggage drop-off.",id,true);bag=true;bagPoint=Point(point);}
                if(kind=="bag-pickup"){if(!bag)Issue("luggage","Luggage pickup must follow drop-off.",id,true);else if(bagPoint is not null && RouteService.Distance(bagPoint,point)>20)Issue("luggage","Pickup and drop-off locations differ. Please check them.",id,true);bag=false;}
                a["arrival"]=Time(begin);a["departure"]=Time(begin+queue+duration);a["scheduledStart"]=begin;a["travelMinutes"]=minutes;a["transportLabel"]=leg.Str("mode")=="transit"?"Public transport (including connections and waiting)":"Walking";a["duration"]=duration;a["cost"]=cost;
                a["queueSource"]=kind=="attraction"?(a["queueMinutes"] is null?"Unknown":"Your planning assumption, not live queue data"):"Not applicable";
                travel+=minutes;queueTotal+=queue;steps+=(int)Math.Ceiling(leg.Number("walkingMeters",leg.Number("meters"))/.75)+(kind=="attraction"?(RecommendationQuality.IsMeal(point)?duration*2:duration*12):kind=="meal"&&RecommendationQuality.IsMeal(point)?duration*2:0);cents+=(long)Math.Round(cost*100);
                stops.Add(new JsonObject{["id"]=id,["name"]=a.Str("name"),["lat"]=point.Number("lat"),["lng"]=point.Number("lng"),["tags"]=point["tags"]?.DeepClone(),["sourceURL"]=point["sourceURL"]?.DeepClone(),["costKnown"]=point["costKnown"]?.DeepClone(),["indoorKnown"]=point["indoorKnown"]?.DeepClone(),["activityType"]=RecommendationQuality.IsMeal(point)?"meal":null,["placeId"]=a["placeId"]?.DeepClone(),["category"]=kind=="attraction"||RecommendationQuality.IsMeal(point)?point.Str("category"):KindLabel(kind),["indoor"]=kind!="attraction"||point.Bool("indoor"),["visitMinutes"]=duration,["cost"]=cost,["note"]=kind=="attraction"||RecommendationQuality.IsMeal(point)?point.Str("note"):"Facility to confirm / planning placeholder, not a booking",["arrival"]=a.Str("arrival"),["departure"]=a.Str("departure"),["hoursVerified"]=a.Bool("hoursVerified"),["mapsURL"]="",["steps"]=(int)Math.Ceiling(leg.Number("walkingMeters",leg.Number("meters"))/.75)});
                legs.Add(leg);previous=point;clock=begin+queue+duration+(walking is null?0:15);
            }
            var back=transit.Get(previous,end,date,clock)??walking?.Get(previous,end)??RouteService.EstimateLeg(previous,end);if(walking is null)back["source"]="Sample distance estimate, not road navigation";if(back.Bool("unreachable"))throw new ServiceException("The end point cannot be reached on foot. Please adjust your schedule.");legs.Add(back);travel+=(int)back.Number("minutes");steps+=(int)Math.Ceiling(back.Number("walkingMeters",back.Number("meters"))/.75);clock+=(int)back.Number("minutes")+(walking is null?0:10);
            if(clock-lastBathroom>settings.Int("restroomInterval"))Issue("restroom-gap","A restroom stop is missing before the return journey.");
            if(bag)Issue("luggage","Luggage pickup has not been scheduled after drop-off.",blocking:true);
            if(clock>endM)Issue("end-time",$"Estimated finish at {Time(clock)}, later than {Time(endM)}.",blocking:true);
            if(steps>dailySteps)Issue("steps",$"{steps} steps, exceeding your energy limit by {steps-dailySteps} steps.",blocking:walking is not null);
            var costTotal=cents/100d;if(costTotal>dailyBudget)Issue("budget",$"Estimated A${costTotal:0.00}, exceeding the daily budget by A${costTotal-dailyBudget:0.00}.",blocking:walking is not null);
            if(travel+queueTotal>settings.Int("timeLimit"))Issue("time-budget",$"Transport and queues: {travel+queueTotal} minutes, exceeding the limit by {travel+queueTotal-settings.Int("timeLimit")} minutes.",blocking:walking is not null);
            if(unknown)Issue("unknown-queue","Queue times are unknown. The time budget shows known subtotals and may be exceeded.");
            if(facilityUnknown||d<count-1&&!stay.Bool("confirmed"))Issue("unconfirmed","Accommodation and facilities are placeholders. Confirm their locations, prices and service hours.");
            days.Add(new JsonObject{["date"]=date,["startMinute"]=startM,["endMinute"]=endM,["startOverride"]=input?.Bool("startOverride")==true,["endOverride"]=input?.Bool("endOverride")==true,["start"]=start.DeepClone(),["end"]=end.DeepClone(),["stay"]=stay,["activities"]=activities,["transportMinutes"]=travel,["queueMinutes"]=queueTotal,["timeUsed"]=travel+queueTotal,["timeRemaining"]=settings.Int("timeLimit")-travel-queueTotal,["unknownQueue"]=unknown,["estimatedSteps"]=steps,["estimatedCost"]=costTotal,["finishTime"]=Time(clock)});
            if(input?["maxSteps"] is not null)days[days.Count-1]!["maxSteps"]=dailySteps;
            if(input?["dailyBudget"] is not null)days[days.Count-1]!["dailyBudget"]=dailyBudget;
            legacy.Add(new JsonObject{["date"]=date,["planningStartMinute"]=startM,["executionStarted"]=previousDays?[d]?.Bool("executionStarted")??false,["stops"]=stops,["legs"]=legs,["estimatedCost"]=costTotal,["estimatedSteps"]=steps,["finishTime"]=Time(clock),["warnings"]=new JsonArray(walking is null ? "Sample timeline: facilities need checking and transport is estimated. This is not a navigation route.":"Transport uses Apple walking directions. Facilities, costs and queues still need checking."),["start"]=start.DeepClone(),["end"]=end.DeepClone()});
            previousStay=Copy(stay);totalCost+=costTotal;
        }
        foreach(var old in previousDays?.OfType<JsonObject>()??[])foreach(var past in old.Array("completedStops")?.OfType<JsonObject>()??[])selected.Add(past.Str("placeId",past.Str("id")));
        foreach(var id in must.Except(selected))issues.Add(new JsonObject{["code"]="must-visit",["message"]="Must-see places not scheduled: "+id,["day"]=0,["blocking"]=true});
        if(totalCost>settings.Number("totalBudget"))issues.Add(new JsonObject{["code"]="total-budget",["message"]=$"Total trip estimate: A${totalCost:0.00}, exceeding the total budget by A${totalCost-settings.Number("totalBudget"):0.00}.",["day"]=-1,["blocking"]=walking is not null});
        trip["schedule"]=new JsonObject{["version"]=N(request,"version",0,0,int.MaxValue-1)+1,["settings"]=settings,["days"]=days,["candidates"]=candidates,["issues"]=issues,["totalCost"]=totalCost,["rainy"]=rainy,["source"]="Local demo: queues unknown, facilities unverified and transport estimated. This is not road navigation.",["radar"]=new JsonArray(Radar("Spending flexibility",settings.Number("dailyBudget")/200),Radar("Walking tolerance",settings.Int("maxSteps")/15000d),Radar("Transport and waiting",settings.Int("timeLimit")/300d),Radar("Trip intensity",pace=="relaxed"?.25:pace=="packed"?.9:.55),Radar("Outdoor preference",interests.Contains("自然")?.8:.4))};
        trip["warnings"]=new JsonArray("Local sample timeline. Facilities, accommodation and queues need checking. Transport is estimated, without road navigation. All costs are per-person estimates. Accommodation covers the first N−1 nights; multiple rooms, groups and overnight activities are not handled.");
        trip["weather"]=new JsonObject{["summary"]=rainy?"Rain scenario (demo)":"Sunny scenario (demo)",["rainy"]=rainy,["source"]="User-selected test scenario, not live weather",["status"]="demo"};
        for(var i=0;i<legacy.Count;i++)if(previousDays?[i] is JsonObject old && old.Array("completedStops") is {} past){legacy[i]!["completedStops"]=past.DeepClone();if(old.Array("skippedStops") is {} skipped)legacy[i]!["skippedStops"]=skipped.DeepClone();}
        trip["days"]=legacy;trip["profile"]=profile;trip["explanation"]="A multi-day timeline draft based on your preferences. Move, resize and select activities, then confirm to save.";
        if(walking is not null){trip["warnings"]=new JsonArray("Walking uses Apple directions. Costs, queues and unverified opening hours still need checking.");trip.Object("schedule")!["source"]="Apple Maps / MapKit walking schedule";}
        if(request["rainy"] is null && request.Object("trip")?.Object("weather") is {} originalWeather)trip["weather"]=originalWeather.DeepClone();
        if(trip["recommendationStatus"] is not null) {
            RecommendationQuality.Assess(profile,legacy);
            var actual=legacy.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).ToArray();
            var missing=interests.Where(i=>!actual.Any(p=>RecommendationQuality.Matches(p,i))).ToArray();
            trip["recommendationStatus"]=missing.Length>0||legacy.OfType<JsonObject>().Any(d=>d.Array("stops")!.Count==0||d.Str("recommendationStatus")=="partial")?"partial":"ready";
            foreach(var interest in missing)trip.Array("warnings")!.Add("Unmet preference: "+RecommendationQuality.InterestLabel(interest)+". Please keep editing the draft.");
            foreach(var day in legacy.OfType<JsonObject>())foreach(var warning in day.Array("warnings")!.OfType<JsonValue>().Where(w=>w.GetValue<string>().Contains("No specific restaurant is scheduled")||w.GetValue<string>().Contains("Core interests are not covered")||w.GetValue<string>().Contains("half-day")))trip.Array("warnings")!.Add(day.Str("date")+": "+warning.GetValue<string>());
        }
        if(inputs is null) {
            // Find a lighter first draft rather than knowingly presenting an overloaded recommendation.
            var result=trip;
            for(var attempt=0;attempt<places.Count;attempt++) {
                var state=result.Object("schedule")!;
                var bad=state.Array("issues")!.OfType<JsonObject>().Where(i=>new[]{"steps","budget","time-budget","end-time","hours","window"}.Contains(i.Str("code"))).Select(i=>i.Int("day")).ToHashSet();
                var ds=(JsonArray)state.Array("days")!.DeepClone();var changed=false;
                for(var i=0;i<ds.Count;i++)if(bad.Contains(i)) {
                    var list=ds[i]!.Array("activities")!;
                    var optional=list.OfType<JsonObject>().LastOrDefault(a=>a.Str("kind")=="attraction"&&!must.Contains(a.Str("placeId")));
                    if(optional is not null){list.Remove(optional);changed=true;}
                }
                if(!changed)break;
                result=Build(new JsonObject{["trip"]=result.DeepClone(),["settings"]=settings.DeepClone(),["days"]=ds,["version"]=state.Int("version"),["rainy"]=rainy});
            }
            return result;
        }
        return trip;
    }
}
