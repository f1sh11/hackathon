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
        if(!double.IsFinite(n)||n!=Math.Truncate(n)||n<min||n>max)throw new ServiceException(key+" 數值不合法。");
        return (int)n;
    }
    static double Cost(JsonObject x,string key,double fallback=0) {
        var n=x[key] is null?fallback:x.Number(key,double.NaN);
        if(!double.IsFinite(n)||n<0||n>100000)throw new ServiceException(key+" 金額不合法。");
        return Math.Round(n,2,MidpointRounding.AwayFromZero);
    }
    static JsonObject Point(JsonObject x)=>new(){["name"]=x.Str("name"),["lat"]=x.Number("lat"),["lng"]=x.Number("lng")};
    static void CheckPoint(JsonObject x) {
        var lat=x.Number("lat",double.NaN);var lng=x.Number("lng",double.NaN);
        if(!double.IsFinite(lat)||!double.IsFinite(lng)||lat is < -37.5 or > -28.0||lng is <141.0 or >154.0||x.Str("name").Length>120)throw new ServiceException("請填入新南威爾斯州 (NSW) 範圍的有效地點與座標。");
    }
    public static JsonObject Attraction(JsonObject p,string pace)=>new(){["id"]=Guid.NewGuid().ToString("N"),["placeId"]=p.Str("id"),["kind"]=PoiService.IsImported(p)?PoiService.ActivityKind(p):RecommendationQuality.IsMeal(p)?"meal":"attraction",["name"]=p.Str("name"),["lat"]=p.Number("lat"),["lng"]=p.Number("lng"),["cost"]=p.Number("cost"),["openMinute"]=p.Int("openMinute"),["closeMinute"]=p.Int("closeMinute"),["duration"]=Math.Clamp(p.Int("visitMinutes")+(pace=="relaxed"?15:pace=="packed"?-10:0),15,180),["queueMinutes"]=null,["locked"]=false};
    static JsonObject Facility(string kind,string name,JsonObject p,int duration,double cost=0,int open=0,int close=1440)=>new(){["id"]=Guid.NewGuid().ToString("N"),["kind"]=kind,["name"]=name,["lat"]=p.Number("lat"),["lng"]=p.Number("lng"),["duration"]=duration,["cost"]=cost,["confirmed"]=false,["openMinute"]=open,["closeMinute"]=close,["locked"]=false};
    static JsonObject Radar(string name,double value)=>new(){["name"]=name,["value"]=Math.Clamp(value,0,1)};
    public static string KindLabel(string kind)=>kind switch {"meal"=>"用餐","restroom"=>"廁所","rest"=>"休息","stay"=>"入住","checkout"=>"退房","bag-drop"=>"寄放","bag-pickup"=>"取件",_=>"景點"};

    public JsonObject Build(JsonObject request,bool allowHistoricalTransit=false) {
        var trip=Copy(request.Object("trip")??throw new ServiceException("缺少行程。"));
        if(trip.Str("mode")=="live")throw new ServiceException("Google 来源行程请使用匹配的道路服务编辑。");
        var evidence=request.Array("walkingRoutes")??trip.Array("walkingRoutes");
        var walking=evidence is null ? null:new WalkingEvidence(evidence);
        if(trip.Str("mode")!="demo" && walking is null)throw new ServiceException("在线行程修改需要真实步行数据。");
        if(evidence is not null)trip["walkingRoutes"]=evidence.DeepClone();
        var transportInput=request.Array("transportRoutes")??trip.Array("transportRoutes");
        var transit=new TransportEvidence(transportInput,allowHistoricalTransit || request.Array("transportRoutes") is null);
        if(transportInput is not null)trip["transportRoutes"]=transportInput.DeepClone();
        var previousDays=(JsonArray?)trip.Array("days")?.DeepClone();
        var places=PlanningCandidates.Merge(catalog.Get(),trip.Array("planningCandidates")).ToList();
        var imported=request.Array("candidates")??trip.Object("schedule")?.Array("candidates");
        if(imported is not null) {
            if(imported.Count>150)throw new ServiceException("工具箱最多 150 個地點。");
            foreach(var p in imported.OfType<JsonObject>().Where(PoiService.IsImported)) {
                var authoritative=p.Str("id").StartsWith("restaurant:") ? restaurants?.Get(p.Str("id"))??throw new ServiceException("餐廳資料不存在，請重新搜尋。") : p;
                var normalized=PoiService.Import(authoritative);if(places.All(x=>x.Str("id")!=normalized.Str("id")))places.Add(normalized);
            }
        }
        // The legacy Rocks estimate includes food; meals have their own allowance here.
        foreach(var place in places.Where(p=>p.Str("id")=="rocks")) {place["cost"]=0;place["note"]="街區遊覽不含餐飲；餐費由用餐站另計，避免重複預留。";}
        var profile=PlanValidator.Validate(trip.Object("profile")??throw new ServiceException("缺少偏好。"),places);
        profile["request"]=profile.Str("request");
        var settings=Copy(request.Object("settings")??trip.Object("schedule")?.Object("settings")??new());
        var pace=settings.Str("pace","balanced");if(pace is not("relaxed" or "balanced" or "packed"))throw new ServiceException("行程節奏不合法。");
        settings["pace"]=pace;settings["timeLimit"]=N(settings,"timeLimit",180,0,1440);settings["restroomInterval"]=N(settings,"restroomInterval",120,30,360);
        settings["mealCost"]=Cost(settings,"mealCost",25);settings["dailyBudget"]=Cost(settings,"dailyBudget",profile.Number("budget"));
        settings["totalBudget"]=Cost(settings,"totalBudget",profile.Number("budget")*profile.Int("days"));settings["maxSteps"]=N(settings,"maxSteps",profile.Int("maxSteps"),500,60000);
        settings["luggage"]=settings.Bool("luggage");
        var rainy=request.Bool("rainy",trip.Object("weather")?.Bool("rainy")??false);
        var must=profile.Array("mustVisit")!.Select(x=>x!.GetValue<string>()).ToHashSet();
        var interests=profile.Array("interests")!.Select(x=>x!.GetValue<string>()).ToHashSet();
        var ranked=places.OrderByDescending(x=>(must.Contains(x.Str("id"))?100:0)+(interests.Contains(x.Str("category"))?20:0)+(rainy?(x.Bool("indoor")?15:-20):0)).ThenBy(x=>RouteService.Distance(profile.Object("start")!,x)).ToArray();
        var candidates=new JsonArray();foreach(var place in ranked){var p=Copy(place);p["reason"]=(interests.Contains(p.Str("category"))?"符合你的興趣；":"區域順路候選；")+(p["indoorKnown"] is not null&&!p.Bool("indoorKnown")?"室内／室外情况未核实":p.Bool("indoor")?"室內活動":rainy?"戶外，雨天需留意":"戶外活動");candidates.Add(p);}
        var inputs=request.Array("days");var count=profile.Int("days");if(inputs is not null&&inputs.Count!=count)throw new ServiceException("天數不一致。");
        var days=new JsonArray();var legacy=new JsonArray();var issues=new JsonArray();var selected=new HashSet<string>();var autoUsed=new HashSet<string>();double totalCost=0;
        var date0=DateOnly.ParseExact(profile.Str("date"),"yyyy-MM-dd",CultureInfo.InvariantCulture);var previousStay=Point(profile.Object("start")!);
        for(var d=0;d<count;d++){
            var date=date0.AddDays(d).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);var input=inputs?[d] as JsonObject;
            var stay=Copy(input?.Object("stay")??new JsonObject{["name"]="住宿待確認",["lat"]=profile.Object("end")!.Number("lat"),["lng"]=profile.Object("end")!.Number("lng"),["cost"]=0,["confirmed"]=false});
            CheckPoint(stay);stay["cost"]=Cost(stay,"cost");stay["checkIn"]=N(stay,"checkIn",900,0,1439);stay["checkOut"]=N(stay,"checkOut",660,0,1439);
            var start=(previousDays?[d]?.Bool("executionStarted")==true || input?.Bool("startOverride")==true) && input?.Object("start") is {} requestedStart ? Point(requestedStart):d==0?Point(profile.Object("start")!):Point(previousStay);CheckPoint(start);var end=input?.Bool("endOverride")==true && input.Object("end") is {} requestedEnd ? Point(requestedEnd):d==count-1?Point(profile.Object("end")!):Point(stay);CheckPoint(end);
            var dailySteps=N(input??new JsonObject(),"maxSteps",settings.Int("maxSteps"),500,60000);var dailyBudget=Cost(input??new JsonObject(),"dailyBudget",settings.Number("dailyBudget"));
            var startM=N(input??profile,"startMinute",profile.Int("startMinute"),0,1439);var endM=N(input??profile,"endMinute",profile.Int("endMinute"),startM+1,1440);
            JsonArray activities;
            if(input is not null)activities=(JsonArray)(input.Array("activities")??throw new ServiceException("缺少活動清單。")).DeepClone();
            else {
                activities=new();if(settings.Bool("luggage"))activities.Add(Facility("bag-drop","寄放行李 · 待確認",start,10));
                var picks=ranked.Where(p=>!RecommendationQuality.IsMeal(p)&&!autoUsed.Contains(p.Str("id"))&&(!rainy||p.Bool("indoor")||must.Contains(p.Str("id")))).Take(pace=="relaxed"?1:pace=="packed"?3:2).ToList();
                if(d==count-1)picks.AddRange(ranked.Where(p=>must.Contains(p.Str("id"))&&!autoUsed.Contains(p.Str("id"))&&!picks.Contains(p)));
                var anchor=start;
                foreach(var p in picks){
                    if(activities.OfType<JsonObject>().Count(a=>a.Str("kind")=="attraction")==1&&startM<840&&endM>780)activities.Add(Facility("meal","午餐 · 餐廳待選",anchor,45,settings.Number("mealCost"),720,840));
                    activities.Add(Attraction(p,pace));autoUsed.Add(p.Str("id"));anchor=p;
                }
                if(!activities.OfType<JsonObject>().Any(a=>a.Str("kind")=="meal")&&startM<840&&endM>780)activities.Add(Facility("meal","午餐 · 餐廳待選",anchor,45,settings.Number("mealCost"),720,840));
                if(endM>=1140)activities.Add(Facility("meal","晚餐 · 餐廳待選",anchor,45,settings.Number("mealCost"),1020,1200));
                if(settings.Bool("luggage"))activities.Add(Facility("bag-pickup","領取行李 · 待確認",start,10));
                if(d<count-1)activities.Add(Facility("stay","入住 · "+stay.Str("name"),stay,15,0,stay.Int("checkIn"),1380));
                if(d>0 && (d==count-1 || RouteService.Distance(previousStay,stay)>20))activities.Insert(0,Facility("checkout","退房 · "+previousStay.Str("name"),previousStay,10,0,0,previousStay.Int("checkOut",660)));
                activities.Insert(Math.Min(2,activities.Count),Facility("restroom","廁所停靠 · 設施待確認",picks.FirstOrDefault()??start,10));
                if(pace=="relaxed")activities.Insert(Math.Min(4,activities.Count),Facility("rest","休息／自由時間",anchor,20));
            }
            if(inputs is null) {
                // Schedule restroom opportunities throughout the day, including long waits before meals.
                var expanded=new JsonArray();var approximateClock=startM;var last=startM;var anchor=start;
                foreach(var original in activities.OfType<JsonObject>()) {
                    var a=Copy(original);var point=a.Str("kind")=="attraction" ? places.First(p=>p.Str("id")==a.Str("placeId")) : a;
                    var begin=approximateClock+(int)RouteService.EstimateLeg(anchor,point).Number("minutes");
                    var open=a.Str("kind")=="attraction" ? point.Int("openMinute") : a.Int("openMinute");begin=Math.Max(begin,open);
                    var attempts=0;
                    while(a.Str("kind")!="restroom" && approximateClock<endM && begin+a.Int("duration")-last>settings.Int("restroomInterval") && attempts++<3) {
                        var bath=Facility("restroom","廁所停靠 · 設施待確認",anchor,10);
                        var at=Math.Clamp(Math.Max(approximateClock,Math.Min(begin,last+settings.Int("restroomInterval")-10)),0,1439);
                        bath["fixedStart"]=at;expanded.Add(bath);last=at+10;approximateClock=last;
                        begin=Math.Max(approximateClock+(int)RouteService.EstimateLeg(anchor,point).Number("minutes"),open);
                        if(begin+a.Int("duration")-last>settings.Int("restroomInterval") && begin<=approximateClock)break;
                    }
                    expanded.Add(a);approximateClock=begin+a.Int("duration");anchor=point;if(a.Str("kind")=="restroom")last=approximateClock;
                }
                if(approximateClock+(int)RouteService.EstimateLeg(anchor,end).Number("minutes")-last>settings.Int("restroomInterval"))expanded.Add(Facility("restroom","返程前廁所 · 設施待確認",anchor,10));
                activities=expanded;
            }
            // Stay changes must update dependent arrival/departure task locations as well.
            foreach(var a in activities.OfType<JsonObject>()) {
                if(a.Str("kind")=="stay") {a["name"]="入住 · "+stay.Str("name");a["lat"]=stay.Number("lat");a["lng"]=stay.Number("lng");a["openMinute"]=stay.Int("checkIn");}
                if(a.Str("kind")=="checkout" && d>0) {a["lat"]=previousStay.Number("lat");a["lng"]=previousStay.Number("lng");a["closeMinute"]=previousStay.Int("checkOut",660);}
            }
            if(activities.Count>30||activities.Any(a=>a is not JsonObject))throw new ServiceException("每天最多 30 項有效活動。");
            var history=previousDays?[d]?.Array("completedStops")?.OfType<JsonObject>().ToArray()??[];
            var ids=new HashSet<string>();var stops=new JsonArray();var legs=new JsonArray();var clock=startM;var travel=0;var queueTotal=0;var unknown=false;var steps=history.Sum(s=>s.Int("steps"));var previous=start;var lastBathroom=startM;var bag=false;JsonObject? bagPoint=null;var facilityUnknown=false;
            long cents=(d<count-1?(long)Math.Round(stay.Number("cost")*100):0)+(long)Math.Round(history.Sum(s=>s.Number("cost"))*100);
            void Issue(string code,string text,string? id=null,bool blocking=false)=>issues.Add(new JsonObject{["code"]=code,["message"]=$"第 {d+1} 天：{text}",["day"]=d,["activityId"]=id,["blocking"]=blocking});
            foreach(var a in activities.OfType<JsonObject>()){
                var id=a.Str("id");if(id.Length is <1 or >120||!ids.Add(id))throw new ServiceException("活動 ID 無效或重複。");
                var kind=a.Str("kind");if(kind is not("attraction" or "meal" or "restroom" or "rest" or "bag-drop" or "bag-pickup" or "stay" or "checkout"))throw new ServiceException("活動類型不合法。");
                JsonObject point;
                if(kind=="attraction"||(kind=="meal"&&places.Any(p=>p.Str("id")==a.Str("placeId")))||(a.Str("placeId").StartsWith("osm:")||a.Str("placeId").StartsWith("apple:")||a.Str("placeId").StartsWith("overture:")||a.Str("placeId").StartsWith("restaurant:")||a.Str("placeId").StartsWith("social:")||a.Str("placeId").StartsWith("gem:"))){
                    point=places.FirstOrDefault(p=>p.Str("id")==a.Str("placeId"))??throw new ServiceException("景點不在目錄。");
                    if(!selected.Add(point.Str("id")))throw new ServiceException("同一景點不能重複排入多天。");
                    if(PoiService.IsImported(point)) {if(kind!=PoiService.ActivityKind(point))throw new ServiceException("地點活動類型不一致。");Issue("poi-unknown",point.Str("name")+(point.Str("id").StartsWith("restaurant:")?"的營業時間待確認；餐費使用選餐估算或使用者預留。":kind!="attraction"&&a.Number("cost")>0?"的費用為使用者預留，營業時間仍未知。":"的費用與營業時間未知，預算未包含此地點費用。"),id);}
                    a["name"]=point.Str("name");a["lat"]=point.Number("lat");a["lng"]=point.Number("lng");a["cost"]=PoiService.IsImported(point)&&kind!="attraction"?Cost(a,"cost"):point.Number("cost");a["indoor"]=point.Bool("indoor");
                    if(point.Str("id").StartsWith("restaurant:")&&kind=="meal"){
                        var estimate=point.Object("restaurantData")?.Object("budgetEstimate");
                        if(a.Str("restaurantEstimateId").Length>0){estimate=restaurants?.GetEstimate(a.Str("restaurantEstimateId"))??throw new ServiceException("餐費估算已不存在，請重新選餐。");if(estimate.Str("placeId")!=point.Str("id"))throw new ServiceException("餐費估算不屬於此分店。");}
                        if(estimate is not null && estimate.Str("status") is "ready" or "stale" && estimate.Str("currency")=="AUD"&&estimate.Str("channel")=="dine_in"){
                            var valid=RestaurantService.Fresh(estimate);
                            a["cost"]=estimate.Number("perPerson");a["restaurantEstimateId"]=estimate.Str("id");a["costBasis"]=valid?"selected_menu_per_person":"stale_estimate";var snapshot=estimate.DeepClone().AsObject();snapshot["status"]=valid?"ready":"stale";a["priceEstimate"]=snapshot;
                            if(!valid)Issue("restaurant-price-expired","餐廳價格已過期，目前保留歷史估算並標記過期，請重新選餐。",id);
                        }
                    }
                }else{point=a;CheckPoint(point);facilityUnknown|=!a.Bool("confirmed");}
                var duration=N(a,"duration",30,5,240);var cost=Cost(a,"cost");var leg=transit.Get(previous,point,date,clock)??walking?.Get(previous,point)??RouteService.EstimateLeg(previous,point);if(walking is null)leg["source"]="示例距離估算，非道路導航";
                if(leg.Bool("unreachable"))throw new ServiceException("无法步行到达"+a.Str("name")+"，请更换地点。");
                var minutes=(int)leg.Number("minutes");var arrival=clock+minutes+(walking is null?0:10);var fixedStart=a["fixedStart"] is null?(int?)null:N(a,"fixedStart",0,0,1439);
                if(fixedStart.HasValue&&arrival>fixedStart.Value)Issue("appointment",a.Str("name")+"趕不上固定時段。",id,true);
                if(a.Bool("locked")&&a["fixedDuration"] is not null&&N(a,"fixedDuration",duration,5,240)!=duration)Issue("locked",a.Str("name")+"的固定停留長度已改變。",id,true);
                var begin=Math.Max(arrival,fixedStart??arrival);var queue=0;
                if(kind=="attraction" || (kind=="meal" && (RecommendationQuality.IsMeal(point)||PoiService.IsImported(point)))){
                    if(a["queueMinutes"] is null)unknown=true;else queue=N(a,"queueMinutes",0,0,360);
                    var windows=RouteScheduler.OpeningWindows(point,date,out var verified);
                    if(kind=="meal" && RecommendationQuality.MealWindow(new JsonObject{["startMinute"]=startM,["endMinute"]=endM}) is {} meal)windows=windows.Select(w=>(Open:Math.Max(w.Open,meal.Open),Close:Math.Min(w.Close,meal.Close))).ToArray();
                    if(PoiService.IsImported(point)) {windows=new[]{(Open:0,Close:1440)};verified=false;}var fits=windows.Where(w=>Math.Max(begin,w.Open)+queue+duration<=w.Close).OrderBy(w=>w.Open).ToArray();
                    if(fits.Length==0)Issue("hours",a.Str("name")+"無法在營業時間內完成等待與遊覽。",id,true);
                    else if(fixedStart.HasValue&&begin<fits[0].Open)Issue("hours",a.Str("name")+"指定時段尚未開門。",id,true);else begin=Math.Max(begin,fits[0].Open);
                    a["hoursVerified"]=verified;if(rainy&&!point.Bool("indoor")&&(point["indoorKnown"] is null||point.Bool("indoorKnown")))Issue("weather",a.Str("name")+"为户外活动；有降雨风险，请出发前确认天气。",id);
                }else{
                    var open=N(a,"openMinute",0,0,1440);var close=N(a,"closeMinute",1440,open,1440);
                    if(!fixedStart.HasValue)begin=Math.Max(begin,open);
                    if(begin<open||begin+duration>close)Issue("window",a.Str("name")+"超過服務／用餐窗口。",id,true);
                }
                if(begin+queue-lastBathroom>settings.Int("restroomInterval"))Issue("restroom-gap","廁所間隔超出偏好，請移動或增加停靠。",id);
                if(kind=="restroom")lastBathroom=begin+duration;
                if(kind=="bag-drop"){if(bag)Issue("luggage","重複寄放。",id,true);bag=true;bagPoint=Point(point);}
                if(kind=="bag-pickup"){if(!bag)Issue("luggage","取件必須在寄放之後。",id,true);else if(bagPoint is not null && RouteService.Distance(bagPoint,point)>20)Issue("luggage","取件位置與寄放點不同，請確認。",id,true);bag=false;}
                a["arrival"]=Time(begin);a["departure"]=Time(begin+queue+duration);a["scheduledStart"]=begin;a["travelMinutes"]=minutes;a["transportLabel"]=leg.Str("mode")=="transit"?"公共交通（含接驳与等候）":"步行";a["duration"]=duration;a["cost"]=cost;
                a["queueSource"]=kind=="attraction"?(a["queueMinutes"] is null?"未知":"使用者規劃假設，非即時排隊"):"不適用";
                travel+=minutes;queueTotal+=queue;steps+=(int)Math.Ceiling(leg.Number("walkingMeters",leg.Number("meters"))/.75)+(kind=="attraction"?(RecommendationQuality.IsMeal(point)?duration*2:duration*12):kind=="meal"&&RecommendationQuality.IsMeal(point)?duration*2:0);cents+=(long)Math.Round(cost*100);
                stops.Add(new JsonObject{["id"]=id,["name"]=a.Str("name"),["lat"]=point.Number("lat"),["lng"]=point.Number("lng"),["tags"]=point["tags"]?.DeepClone(),["sourceURL"]=point["sourceURL"]?.DeepClone(),["costKnown"]=point["costKnown"]?.DeepClone(),["indoorKnown"]=point["indoorKnown"]?.DeepClone(),["activityType"]=RecommendationQuality.IsMeal(point)?"meal":null,["placeId"]=a["placeId"]?.DeepClone(),["category"]=kind=="attraction"||RecommendationQuality.IsMeal(point)?point.Str("category"):KindLabel(kind),["indoor"]=kind!="attraction"||point.Bool("indoor"),["visitMinutes"]=duration,["cost"]=cost,["note"]=kind=="attraction"||RecommendationQuality.IsMeal(point)?point.Str("note"):"待確認設施／規劃佔位，非預約",["arrival"]=a.Str("arrival"),["departure"]=a.Str("departure"),["hoursVerified"]=a.Bool("hoursVerified"),["mapsURL"]="",["steps"]=(int)Math.Ceiling(leg.Number("walkingMeters",leg.Number("meters"))/.75)});
                legs.Add(leg);previous=point;clock=begin+queue+duration+(walking is null?0:15);
            }
            var back=transit.Get(previous,end,date,clock)??walking?.Get(previous,end)??RouteService.EstimateLeg(previous,end);if(walking is null)back["source"]="示例距離估算，非道路導航";if(back.Bool("unreachable"))throw new ServiceException("无法步行返回终点，请修改安排。");legs.Add(back);travel+=(int)back.Number("minutes");steps+=(int)Math.Ceiling(back.Number("walkingMeters",back.Number("meters"))/.75);clock+=(int)back.Number("minutes")+(walking is null?0:10);
            if(clock-lastBathroom>settings.Int("restroomInterval"))Issue("restroom-gap","返程前廁所安排有缺口。");
            if(bag)Issue("luggage","寄放後尚未安排取件。",blocking:true);
            if(clock>endM)Issue("end-time",$"預估 {Time(clock)} 結束，超過 {Time(endM)}。",blocking:true);
            if(steps>dailySteps)Issue("steps",$"步數 {steps}，超出精力上限 {steps-dailySteps} 步。",blocking:walking is not null);
            var costTotal=cents/100d;if(costTotal>dailyBudget)Issue("budget",$"預估 A${costTotal:0.00}，超出每日預算 A${costTotal-dailyBudget:0.00}。",blocking:walking is not null);
            if(travel+queueTotal>settings.Int("timeLimit"))Issue("time-budget",$"交通＋排隊 {travel+queueTotal} 分，超出 {travel+queueTotal-settings.Int("timeLimit")} 分。",blocking:walking is not null);
            if(unknown)Issue("unknown-queue","排隊未知；時間額度為已知小計，不能保證未超限。");
            if(facilityUnknown||d<count-1&&!stay.Bool("confirmed"))Issue("unconfirmed","住宿／生活設施是佔位，位置、價格與服務時間需確認。");
            days.Add(new JsonObject{["date"]=date,["startMinute"]=startM,["endMinute"]=endM,["startOverride"]=input?.Bool("startOverride")==true,["endOverride"]=input?.Bool("endOverride")==true,["start"]=start.DeepClone(),["end"]=end.DeepClone(),["stay"]=stay,["activities"]=activities,["transportMinutes"]=travel,["queueMinutes"]=queueTotal,["timeUsed"]=travel+queueTotal,["timeRemaining"]=settings.Int("timeLimit")-travel-queueTotal,["unknownQueue"]=unknown,["estimatedSteps"]=steps,["estimatedCost"]=costTotal,["finishTime"]=Time(clock)});
            if(input?["maxSteps"] is not null)days[days.Count-1]!["maxSteps"]=dailySteps;
            if(input?["dailyBudget"] is not null)days[days.Count-1]!["dailyBudget"]=dailyBudget;
            legacy.Add(new JsonObject{["date"]=date,["planningStartMinute"]=startM,["executionStarted"]=previousDays?[d]?.Bool("executionStarted")??false,["stops"]=stops,["legs"]=legs,["estimatedCost"]=costTotal,["estimatedSteps"]=steps,["finishTime"]=Time(clock),["warnings"]=new JsonArray(walking is null ? "時間軸示例：設施待確認，交通為估算，不是導航路線。":"交通按 Apple 步行查询安排，设施、费用和排队仍需确认。"),["start"]=start.DeepClone(),["end"]=end.DeepClone()});
            previousStay=Copy(stay);totalCost+=costTotal;
        }
        foreach(var old in previousDays?.OfType<JsonObject>()??[])foreach(var past in old.Array("completedStops")?.OfType<JsonObject>()??[])selected.Add(past.Str("placeId",past.Str("id")));
        foreach(var id in must.Except(selected))issues.Add(new JsonObject{["code"]="must-visit",["message"]="必去景點尚未安排："+id,["day"]=0,["blocking"]=true});
        if(totalCost>settings.Number("totalBudget"))issues.Add(new JsonObject{["code"]="total-budget",["message"]=$"全旅程預估 A${totalCost:0.00}，超出總預算 A${totalCost-settings.Number("totalBudget"):0.00}。",["day"]=-1,["blocking"]=walking is not null});
        trip["schedule"]=new JsonObject{["version"]=N(request,"version",0,0,int.MaxValue-1)+1,["settings"]=settings,["days"]=days,["candidates"]=candidates,["issues"]=issues,["totalCost"]=totalCost,["rainy"]=rainy,["source"]="本機示例：排隊未知、設施待確認；交通估算，不是道路導航。",["radar"]=new JsonArray(Radar("消費彈性",settings.Number("dailyBudget")/200),Radar("步行接受度",settings.Int("maxSteps")/15000d),Radar("交通與等待",settings.Int("timeLimit")/300d),Radar("行程緊湊度",pace=="relaxed"?.25:pace=="packed"?.9:.55),Radar("戶外偏好",interests.Contains("自然")?.8:.4))};
        trip["warnings"]=new JsonArray("本機時間軸示例；設施、住宿與排隊需確認。交通估算，不提供道路導航。所有金額為單人估算。住宿計入前 N−1 晚，未處理多房／多人或跨午夜活動。");
        trip["weather"]=new JsonObject{["summary"]=rainy?"下雨示例情境":"晴天示例情境",["rainy"]=rainy,["source"]="使用者測試情境，非即時天氣",["status"]="demo"};
        for(var i=0;i<legacy.Count;i++)if(previousDays?[i] is JsonObject old && old.Array("completedStops") is {} past){legacy[i]!["completedStops"]=past.DeepClone();if(old.Array("skippedStops") is {} skipped)legacy[i]!["skippedStops"]=skipped.DeepClone();}
        trip["days"]=legacy;trip["profile"]=profile;trip["explanation"]="依偏好產生多日時間軸草案，可拖動、拉伸及勾選景點；確認後才儲存。";
        if(walking is not null){trip["warnings"]=new JsonArray("步行已按 Apple 道路查询；费用、排队和未核实营业时段仍需确认。");trip.Object("schedule")!["source"]="Apple Maps / MapKit 步行排程";}
        if(request["rainy"] is null && request.Object("trip")?.Object("weather") is {} originalWeather)trip["weather"]=originalWeather.DeepClone();
        if(trip["recommendationStatus"] is not null) {
            RecommendationQuality.Assess(profile,legacy);
            var actual=legacy.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).ToArray();
            var missing=interests.Where(i=>!actual.Any(p=>RecommendationQuality.Matches(p,i))).ToArray();
            trip["recommendationStatus"]=missing.Length>0||legacy.OfType<JsonObject>().Any(d=>d.Array("stops")!.Count==0||d.Str("recommendationStatus")=="partial")?"partial":"ready";
            foreach(var interest in missing)trip.Array("warnings")!.Add("未满足「"+interest+"」偏好，请继续修改草案。");
            foreach(var day in legacy.OfType<JsonObject>())foreach(var warning in day.Array("warnings")!.OfType<JsonValue>().Where(w=>w.GetValue<string>().Contains("未安排")||w.GetValue<string>().Contains("未覆盖")||w.GetValue<string>().Contains("半日")))trip.Array("warnings")!.Add(day.Str("date")+"："+warning.GetValue<string>());
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
