using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public interface IFeasibilityTools {
    Task<JsonObject> CallAsync(string tool,JsonObject trip,JsonObject profile,CancellationToken ct);
}

/// Read-only tools. Returned observations never change a user's plan or preferences.
public sealed class FeasibilityTools(PlaceCatalog catalog,PoiService pois,TripTransportRouter transit):IFeasibilityTools
{
    public async Task<JsonObject> CallAsync(string tool,JsonObject trip,JsonObject profile,CancellationToken ct) => tool switch {
        "constraint_check"=>Constraints(trip,profile),
        "route_evidence"=>Routes(trip),
        "place_evidence"=>await PlacesAsync(trip,ct),
        "refresh_transit"=>await RefreshTransitAsync(trip,profile,ct),
        _=>throw new ServiceException("Unknown feasibility tool.")
    };
    public static string Identity(JsonObject place)=>place.Str("placeId").Length>0?place.Str("placeId"):place.Str("id");
    public static JsonObject Issue(string code,int day=-1,string severity="blocking",double? actual=null,double? limit=null) {
        var target=code is "budget_basis_requires_normalization" or "daily_time_window_conflict" or "date_requires_confirmation"?"requirements_agent":"itinerary_planner";
        var action=code.Contains("budget")?"replace_optional_paid_stop":code.Contains("walking_evidence")?"request_mapkit_evidence":code.Contains("transit")?"refresh_or_change_transport":code=="must_visit_missing"?"include_required_place":code.Contains("time")?"reorder_or_shorten_optional_stops":"review_affected_details";
        var x=new JsonObject{["code"]=code,["dayIndex"]=day,["severity"]=severity,["targetAgent"]=target,["suggestedAction"]=action};
        if(actual is not null)x["actual"]=actual;if(limit is not null)x["limit"]=limit;x["message"]=FeasibilityCopy.Message(code,day,actual,limit);x["actionLabel"]=FeasibilityCopy.Action(action);return x;
    }
    public static JsonObject Result(JsonArray issues,JsonObject? observations=null)=>new(){["issues"]=issues,["observations"]=observations ?? new()};
    public static JsonObject Constraints(JsonObject trip,JsonObject profile) {
        var issues=new JsonArray();var days=trip.Array("days")!;var all=new HashSet<string>();var physicalPlaces=new List<JsonObject>();
        var needs=profile.Str("companionNeeds")+" "+profile.Str("avoid")+" "+profile.Str("specialLimits");
        if(System.Text.RegularExpressions.Regex.IsMatch(needs,"wheelchair|step.free|无障碍|轮椅|輪椅",System.Text.RegularExpressions.RegexOptions.IgnoreCase))issues.Add(Issue("accessibility_needs_unverified",severity:"needs_info"));
        if(System.Text.RegularExpressions.Regex.IsMatch(needs,"allerg|celiac|coeliac|过敏|過敏",System.Text.RegularExpressions.RegexOptions.IgnoreCase))issues.Add(Issue("dietary_needs_unverified",severity:"needs_info"));
        if(days.Count!=profile.Int("days",days.Count))issues.Add(Issue("travel_day_count_mismatch"));
        var dates=new HashSet<string>();var required=profile.Array("mustVisit")?.Select(x=>x!.GetValue<string>()).ToHashSet() ?? [];
        var avoid=profile.Array("avoidPlaceIds")?.Select(x=>x!.GetValue<string>()).ToHashSet() ?? [];
        int Minute(string text) => TimeOnly.TryParseExact(text,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var t)?t.Hour*60+t.Minute:-1;
        for(var d=0;d<days.Count;d++) {
            var day=days[d]!.AsObject();var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();var legs=day.Array("legs")!.OfType<JsonObject>().ToArray();
            if(trip.Object("schedule")?.Array("days")?.ElementAtOrDefault(d) is JsonObject scheduled && scheduled.Array("activities") is {} activities) {
                var scheduledIds=activities.OfType<JsonObject>().Select(a=>a.Str("placeId")).ToArray();
                if(!scheduledIds.SequenceEqual(stops.Select(Identity)) || Math.Abs(activities.OfType<JsonObject>().Sum(a=>a.Number("cost"))-stops.Sum(x=>x.Number("cost")))>.01)issues.Add(Issue("schedule_snapshot_mismatch",d));
            }
            var budget=profile.Number("budget");var maxSteps=profile.Int("maxSteps");var start=profile.Int("startMinute");var end=profile.Int("endMinute");
            // Explicit requirements remain authoritative; planner-supplied schedule settings cannot relax them.
            if((profile.Str("rhythm")=="轻松随意" || profile.Str("energy")=="低精力") && stops.Length>3)issues.Add(Issue("pace_density",d,"warning"));
            var spent=stops.Sum(x=>x.Number("cost"));
            if(spent>budget)issues.Add(Issue("daily_budget_exceeded",d,actual:spent,limit:budget));
            if(day.Int("estimatedSteps")>maxSteps)issues.Add(Issue("daily_steps_exceeded",d,actual:day.Int("estimatedSteps"),limit:maxSteps));
            if(!dates.Add(day.Str("date")))issues.Add(Issue("duplicate_day",d));
            if(DateOnly.TryParseExact(profile.Str("date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var first) && day.Str("date")!=first.AddDays(d).ToString("yyyy-MM-dd"))issues.Add(Issue("travel_date_mismatch",d));
            var clock=Math.Max(start,day.Int("planningStartMinute",start));
            for(var i=0;i<stops.Length;i++) {
                var stop=stops[i];var id=Identity(stop);
                if(!all.Add(id) || (!RouteLogistics.IsTask(stop) && physicalPlaces.Any(p=>PlaceIdentity.Same(p,stop))))issues.Add(Issue("duplicate_place",d));
                if(!RouteLogistics.IsTask(stop))physicalPlaces.Add(stop);
                if(avoid.Contains(id))issues.Add(Issue("avoided_place",d));
                var arrival=Minute(stop.Str("arrival"));var departure=Minute(stop.Str("departure"));
                if(arrival<clock+(int)Math.Ceiling(legs[i].Number("minutes")) || departure<arrival+stop.Int("visitMinutes"))issues.Add(Issue("time_overlap_or_travel_gap",d));
                if(stop.Number("cost")<0 || stop.Int("visitMinutes")<0)issues.Add(Issue("invalid_activity_value",d));
                if(!stop.Bool("hoursVerified"))issues.Add(Issue("opening_hours_unknown",d,"warning"));
                else {
                    var windows=RouteScheduler.OpeningWindows(stop,day.Str("date"),out var verified);
                    if(!verified)issues.Add(Issue("opening_evidence_missing",d,"needs_info"));
                    else if(!windows.Any(w=>arrival>=w.Open && departure<=w.Close))issues.Add(Issue("outside_opening_hours",d));
                }
                if(stop["costKnown"] is null || !stop.Bool("costKnown"))issues.Add(Issue("price_not_verified",d,"warning"));
                clock=departure;
            }
            clock+=(int)Math.Ceiling(legs.Last().Number("minutes"));
            if(clock>end || Minute(day.Str("finishTime"))>end)issues.Add(Issue("return_time_exceeded",d,actual:clock,limit:end));
            if(Minute(day.Str("finishTime"))<clock)issues.Add(Issue("return_time_underreported",d));
            var knownWalk=legs.Sum(x=>x.Number("walkingMeters",x.Str("mode","walk")=="walk"?x.Number("meters"):0));
            var lowerBound=(int)Math.Ceiling(knownWalk/.75);
            if(lowerBound>maxSteps)issues.Add(Issue("walking_distance_exceeds_steps",d,actual:lowerBound,limit:maxSteps));
            if(day.Int("estimatedSteps")<lowerBound)issues.Add(Issue("steps_underreported",d));
            if(profile.Str("transportPreference")=="walk" && legs.Any(x=>x.Str("mode","walk")!="walk"))issues.Add(Issue("transport_preference_violated",d));
        }
        foreach(var _ in required.Except(all))issues.Add(Issue("must_visit_missing"));
        foreach(var issue in trip.Object("schedule")?.Array("issues")?.OfType<JsonObject>().Where(x=>x.Bool("blocking")) ?? [])issues.Add(Issue("schedule_conflict",issue.Int("day",-1)));
        return Result(issues,new(){["dayCount"]=days.Count,["stopCount"]=all.Count,["budgetBasis"]="AUD/person/day activity budget"});
    }
    public static JsonObject Routes(JsonObject trip) {
        var issues=new JsonArray();WalkingEvidence? roads=null;
        try{if(trip.Array("walkingRoutes") is {} observations)roads=new(observations);}catch(ServiceException){issues.Add(Issue("walking_evidence_invalid_or_expired",severity:"needs_info"));}
        var count=0;
        foreach(var (day,d) in trip.Array("days")!.OfType<JsonObject>().Select((day,d)=>(day,d))) {
            var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();var previous=day.Object("start")!;var i=0;
            foreach(var leg in day.Array("legs")!.OfType<JsonObject>()) {
                var next=i<stops.Length?stops[i]:day.Object("end")!;
                if(leg.Bool("unreachable"))issues.Add(Issue("route_unreachable",d));
                if(leg.Str("mode","walk")=="walk") {
                    try {
                        var observation=roads?.Get(previous,next) ?? throw new ServiceException("Missing road observation");
                        if(observation.Bool("unreachable"))issues.Add(Issue("route_unreachable",d));
                        else if(leg.Number("minutes")<observation.Number("minutes") || Math.Abs(leg.Number("meters")-observation.Number("meters"))>5)issues.Add(Issue("route_evidence_mismatch",d));
                        else count++;
                    }catch(ServiceException){issues.Add(Issue("walking_evidence_missing",d,"needs_info"));}
                } else issues.Add(Issue(leg.Str("mode")=="transit"?"transit_refresh_required":"unsupported_transport_evidence",d,"needs_info"));
                previous=next;i++;
            }
        }
        var expiries=trip.Array("walkingRoutes")?.OfType<JsonObject>().Select(x=>DateTimeOffset.TryParse(x.Str("fetchedAt"),out var at)?at.AddHours(6):DateTimeOffset.MinValue).ToArray() ?? [];
        return Result(issues,new(){["validUntil"]=expiries.Length>0?expiries.Min().ToString("O"):DateTimeOffset.UtcNow.ToString("O"),["verifiedDirectionalWalkingLegs"]=count,["attestation"]="client_reported_mapkit_not_server_attested"});
    }
    async Task<JsonObject> PlacesAsync(JsonObject trip,CancellationToken ct) {
        var issues=new JsonArray();var known=catalog.Get().ToDictionary(x=>x.Str("id"));var queries=0;var matched=0;
        foreach(var stop in trip.Array("days")!.OfType<JsonObject>().SelectMany(x=>x.Array("stops")!.OfType<JsonObject>()).DistinctBy(Identity)) {
            ct.ThrowIfCancellationRequested();known.TryGetValue(Identity(stop),out var original);
            if(original is null && queries++<8) {
                var found=await pois.SearchAsync(new(){["lat"]=stop.Number("lat"),["lng"]=stop.Number("lng"),["radius"]=500,["category"]="all",["query"]=stop.Str("name"),["provider"]="osm"},ct);
                original=found.Array("places")?.OfType<JsonObject>().FirstOrDefault(x=>x.Str("id")==Identity(stop));
            }
            if(original is null){issues.Add(Issue("place_identity_unverified",severity:"needs_info"));continue;}
            if(RouteService.Distance(stop,original)>100 || PlanningLocationGuard.HasKnownConflict(original))issues.Add(Issue("place_identity_coordinate_conflict"));else matched++;
            // Planner claims are not authoritative: use source cost/hours only when explicitly known.
            if(original.Bool("costKnown") && Math.Abs(original.Number("cost")-stop.Number("cost"))>.01)issues.Add(Issue("source_price_mismatch"));
        }
        return Result(issues,new(){["matchedPlaces"]=matched,["sourceQueries"]=Math.Min(queries,8)});
    }
    async Task<JsonObject> RefreshTransitAsync(JsonObject trip,JsonObject profile,CancellationToken ct) {
        var issues=new JsonArray();var count=0;var context=new PlanningContext("demo",new Dictionary<string,string>());
        foreach(var (day,d) in trip.Array("days")!.OfType<JsonObject>().Select((x,i)=>(x,i))) {
            var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();var previous=day.Object("start")!;var clock=day.Int("planningStartMinute",profile.Int("startMinute"));var i=0;
            foreach(var leg in day.Array("legs")!.OfType<JsonObject>()) {
                var next=i<stops.Length?stops[i]:day.Object("end")!;
                if(leg.Str("mode","walk")!="walk") {
                    if(count++>=8){issues.Add(Issue("transit_query_limit",d,"needs_info"));break;}
                    var daily=profile.DeepClone().AsObject();daily["date"]=day.Str("date");daily["transportPreference"]="balanced";var current=await transit.ChooseAsync(previous,next,new JsonObject{["unreachable"]=true},daily,clock,context,ct);
                    if(current.Bool("unreachable"))issues.Add(Issue("transit_unavailable",d,"needs_info"));
                    else if(current.Number("minutes")>leg.Number("minutes"))issues.Add(Issue("transit_timing_changed",d,actual:current.Number("minutes"),limit:leg.Number("minutes")));
                }
                clock=i<stops.Length ? TimeOnly.ParseExact(stops[i].Str("departure"),"HH:mm").Hour*60+TimeOnly.ParseExact(stops[i].Str("departure"),"HH:mm").Minute : clock;previous=next;i++;
            }
        }
        return Result(issues,new(){["validUntil"]=DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),["queries"]=Math.Min(count,8),["coverage"]="saved_transit_legs_only"});
    }
}
