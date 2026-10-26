using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace SydneyBuddy.Api;

/// A proposal only. The existing revision is unchanged until the user accepts it.
public static class RemainingJourney
{
    public static JsonObject Build(JsonObject original,JsonObject request,ScheduleEditorService editor,PlaceCatalog catalog,CancellationToken ct=default,bool transportPreflight=false) {
        var index=request.Int("day",-1);var days=original.Array("days")!;
        if(index<0||index>=days.Count)throw new ServiceException("Invalid adjustment date.");
        if(original.Str("mode")=="live")throw new ServiceException("Use a route service that matches the Google places to make adjustments.");
        var evidence=request.Array("walkingRoutes")??throw new ServiceException("Search for actual walking times first.");
        var planningCatalog=PlanningCandidates.Merge(catalog.Get(),request.Array("planningCandidates")??original.Array("planningCandidates")).ToList();foreach(var point in original.Object("schedule")?.Array("candidates")?.OfType<JsonObject>().Where(PoiService.IsImported)??[]){if(planningCatalog.All(p=>p.Str("id")!=point.Str("id")))planningCatalog.Add(PoiService.Import(point));}
        var day=days[index]!.AsObject();var profile=(JsonObject)original.Object("profile")!.DeepClone();
        if(request.Object("constraints") is {} changes){foreach(var key in new[]{"budget","maxSteps","endMinute","mustDo","avoid","interests","transportPreference"})if(changes[key] is not null)profile[key]=changes[key]!.DeepClone();profile=PlanValidator.Validate(profile,planningCatalog);}
        var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();
        var ids=request.Array("completedIds")?.Select(x=>x?.GetValue<string>()??"").ToHashSet()??[];
        var skipped=request.Array("skippedIds")?.Select(x=>x?.GetValue<string>()??"").ToHashSet()??[];
        if(skipped.Overlaps(ids))throw new ServiceException("Completed and skipped statuses conflict.");
        var consumed=ids.Union(skipped).ToHashSet();
        if(consumed.Any(id=>!stops.Any(s=>s.Str("id")==id)))throw new ServiceException("Progress does not match the current trip. Please reload.");
        // Navigation completes an ordered prefix. Arbitrary completion would corrupt the past itinerary.
        var prefix=stops.TakeWhile(s=>consumed.Contains(s.Str("id"))).ToArray();
        var completed=prefix.Where(s=>ids.Contains(s.Str("id"))).ToArray();
        if(prefix.Length!=consumed.Count)throw new ServiceException("Confirm your progress at the current stop before adjusting the rest of the trip.");
        var message=request.Str("request");var origin=request.Object("origin")??day.Object("start")!;
        var start=profile.Int("startMinute");
        if(request["currentMinute"] is not null) {
            var zone=TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");var today=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).ToString("yyyy-MM-dd");
            if(day.Str("date")!=today)throw new ServiceException("On-trip adjustments apply to today only. Edit the daily schedule for other dates.");
            start=request.Int("currentMinute");if(start<0||start>=profile.Int("endMinute"))throw new ServiceException("Today's available time has ended. Extend the end time or keep the return journey.");
        }
        if(consumed.Count>0 && request["currentMinute"] is null)throw new ServiceException("On-trip adjustments need your current time and location.");
        if(day.Array("completedStops")?.Count>0 && request["currentMinute"] is null)throw new ServiceException("On-trip adjustments need your current time and location.");
        var baseline=(JsonObject)original.DeepClone();if(request.Array("planningCandidates") is {} added)baseline["planningCandidates"]=added.DeepClone();baseline.Array("days")![index]!["planningStartMinute"]=start;baseline.Array("days")![index]!["executionStarted"]=true;
        var state=baseline.Object("schedule");
        if(state is null) {
            var ds=new JsonArray();
            foreach(var d in days.OfType<JsonObject>())ds.Add(AdaptDay(d,profile));
            state=new(){["version"]=0,["settings"]=new JsonObject{["pace"]="balanced",["timeLimit"]=1440,["restroomInterval"]=360,["mealCost"]=25,["dailyBudget"]=profile.Number("budget"),["totalBudget"]=profile.Number("budget")*profile.Int("days"),["maxSteps"]=profile.Int("maxSteps"),["luggage"]=false},["days"]=ds,["candidates"]=new JsonArray()};
            baseline["schedule"]=state;
        }
        var daily=state.Array("days")![index]!.AsObject();
        var activities=daily.Array("activities")!;
        foreach(var a in activities.OfType<JsonObject>().Where(a=>consumed.Contains(a.Str("id"))).ToArray())activities.Remove(a);
        var allowed=TravelConstraints.Apply(profile,planningCatalog);var allowedIDs=allowed.Select(p=>p.Str("id")).ToHashSet();
        var knownIDs=planningCatalog.Select(p=>p.Str("id")).ToHashSet();
        foreach(var a in activities.OfType<JsonObject>().Where(a=>knownIDs.Contains(a.Str("placeId"))&&!allowedIDs.Contains(a.Str("placeId"))).ToArray()){
            if(a.Bool("locked"))throw new ServiceException("Your exclusions conflict with a fixed activity. Unlock or update it first.");activities.Remove(a);
        }
        var visited=completed.Concat(day.Array("completedStops")?.OfType<JsonObject>()??[]).Select(s=>s.Str("placeId",original.Object("schedule")?.Array("days")?[index]?.Array("activities")?.OfType<JsonObject>().FirstOrDefault(a=>a.Str("id")==s.Str("id"))?.Str("placeId",s.Str("id"))??s.Str("id"))).ToHashSet();
        var currentPlaces=activities.OfType<JsonObject>().Select(a=>a.Str("placeId")).ToHashSet();
        foreach(var point in allowed.Where(p=>profile.Array("mustVisit")!.Any(id=>id!.GetValue<string>()==p.Str("id"))&&!currentPlaces.Contains(p.Str("id"))&&!visited.Contains(p.Str("id"))))activities.Add(ScheduleEditorService.Attraction(point,"balanced"));
        if(Regex.IsMatch(message,"多一点美食|加.*餐|more food|add.*restaurant",RegexOptions.IgnoreCase)){
            var food=allowed.FirstOrDefault(p=>p.Str("category")=="美食"&&!currentPlaces.Contains(p.Str("id"))&&!visited.Contains(p.Str("id")));
            if(food is not null)activities.Add(ScheduleEditorService.Attraction(food,"balanced"));
        }
        var beforeCount=activities.Count;
        var rainy=request.Bool("rain") || TravelAdjustmentIntent.Rain(message);
        var must=profile.Array("mustVisit")!.Select(x=>x!.GetValue<string>()).ToHashSet();
        if(rainy)foreach(var a in activities.OfType<JsonObject>().Where(a=>a.Str("kind")=="attraction"&&!a.Bool("indoor")&&!a.Bool("locked")&&!must.Contains(a.Str("placeId"))).ToArray())activities.Remove(a);
        daily["start"]=origin.DeepClone();daily["startMinute"]=start;
        var end=profile.Int("endMinute");
        if(TravelAdjustmentIntent.RemainingMinutes(message) is {} limit)end=Math.Min(end,start+limit);
        daily["endMinute"]=end;
        var settings=(JsonObject)state.Object("settings")!.DeepClone();
        var archived=day.Array("completedStops")?.OfType<JsonObject>().ToArray()??[];
        var completedSteps=completed.Concat(archived).Sum(s=>s.Int("steps"));var completedCost=completed.Concat(archived).Sum(s=>s.Number("cost"));
        var steps=profile.Int("maxSteps");if(TravelAdjustmentIntent.Lighter(message))steps=Math.Min(steps,7000);
        if(!transportPreflight&&completedSteps>=steps)throw new ServiceException("You have reached today's step limit. Rest, return or increase the limit.");
        daily["maxSteps"]=transportPreflight?60000:steps;daily["dailyBudget"]=profile.Number("budget");
        var allPast=(JsonArray)(day.Array("completedStops")?.DeepClone()??new JsonArray());
        foreach(var item in completed){var historyStop=(JsonObject)item.DeepClone();historyStop["placeId"]=original.Object("schedule")?.Array("days")?[index]?.Array("activities")?.OfType<JsonObject>().FirstOrDefault(a=>a.Str("id")==item.Str("id"))?.Str("placeId",item.Str("id"))??item.Str("id");allPast.Add(historyStop);}
        baseline.Array("days")![index]!["completedStops"]=allPast.DeepClone();
        baseline.Object("profile")!["mustVisit"]=new JsonArray(must.Where(id=>!completed.Any(s=>s.Str("id")==id)).Select(id=>(JsonNode)JsonValue.Create(id)!).ToArray());
        JsonObject Evaluate(JsonArray list) {
            var ds=(JsonArray)state.Array("days")!.DeepClone();ds[index]!["activities"]=list.DeepClone();
            return editor.Build(new JsonObject{["trip"]=baseline.DeepClone(),["settings"]=settings.DeepClone(),["days"]=ds,["walkingRoutes"]=evidence.DeepClone(),["candidates"]=state["candidates"]?.DeepClone()});
        }
        bool Feasible(JsonObject result)=>!result.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Any(i=>i.Bool("blocking")&&(!transportPreflight||!new[]{"steps","end-time","time-budget","appointment","hours"}.Contains(i.Str("code")))&&(i.Int("day")==index||i.Int("day")==-1));
        JsonObject? best=null;var bestMinutes=int.MaxValue;
        for(var removal=0;removal<=beforeCount;removal++) {
            var flexible=activities.OfType<JsonObject>().Select((a,i)=>(a,i)).Where(x=>!x.a.Bool("locked")&&x.a["fixedStart"] is null).Select(x=>x.i).ToArray();
            var permutations=flexible.Length<=7 ? Orders(flexible):new[]{flexible};
            foreach(var order in permutations) {
                ct.ThrowIfCancellationRequested();
                var arranged=(JsonArray)activities.DeepClone();for(var i=0;i<flexible.Length;i++)arranged[flexible[i]]=activities[order[i]]!.DeepClone();
                try {var candidate=Evaluate(arranged);if(!Feasible(candidate))continue;var minutes=candidate.Object("schedule")!.Array("days")![index]!.Int("transportMinutes");if(minutes<bestMinutes){best=candidate;bestMinutes=minutes;}}
                catch(ServiceException ex) when(ex.Message.StartsWith("Cannot walk")){ }
            }
            if(best is not null)break;
            var optional=activities.OfType<JsonObject>().LastOrDefault(a=>!a.Bool("locked")&&a["fixedStart"] is null&&(a.Str("kind") is "attraction" or "rest")&&!must.Contains(a.Str("placeId")));
            if(optional is null)break;activities.Remove(optional);
        }
        if(best is null)throw new ServiceException("The remaining time, steps and fixed bookings cannot all fit. Extend the end time, adjust must-see places or view the return route. Your original trip is kept.");
        var result=(JsonObject)original.DeepClone();if(request.Array("planningCandidates") is {} addedCandidates)result["planningCandidates"]=addedCandidates.DeepClone();var accepted=(JsonObject)best.Array("days")![index]!.DeepClone();
        var past=allPast;
        var skippedHistory=(JsonArray)(day.Array("skippedStops")?.DeepClone()??new JsonArray());foreach(var item in prefix.Where(s=>skipped.Contains(s.Str("id"))))skippedHistory.Add(item.DeepClone());
        accepted["executionStarted"]=true;accepted["skippedStops"]=skippedHistory;accepted["completedStops"]=past;accepted["planningStartMinute"]=start;
        result.Array("days")![index]=accepted;
        result["walkingRoutes"]=evidence.DeepClone();
        result["schedule"]=best["schedule"]!.DeepClone();
        for(var i=0;i<days.Count;i++)if(i!=index)result.Object("schedule")!.Array("days")![i]=state.Array("days")![i]!.DeepClone();
        result.Object("schedule")!["settings"]=state["settings"]!.DeepClone();
        result.Object("schedule")!["issues"]=new JsonArray(best.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Where(i=>i.Int("day")==index).Select(i=>i.DeepClone()).ToArray());
        result.Object("schedule")!["totalCost"]=result.Array("days")!.OfType<JsonObject>().Sum(d=>d.Number("estimatedCost"));
        result["explanation"]=$"Kept {past.Count} completed places and adjusted only the rest of day {index+1}. Estimated road walking: {bestMinutes} minutes. Opening hours, breaks and the return journey were checked again.";
        if(accepted.Array("stops")!.Count==0){accepted["freeTime"]=true;result["explanation"]=result.Str("explanation")+"Remaining activities do not fit your limits. The return journey is kept; you can rest or relax your limits.";}
        result["profile"]=profile.DeepClone();return result;
    }
    public static JsonObject AdaptDay(JsonObject d,JsonObject profile) {
        int Minute(string s){var x=s.Split(':');return int.Parse(x[0])*60+int.Parse(x[1]);}
        var activities=new JsonArray(d.Array("stops")!.OfType<JsonObject>().Select((s,i)=>(JsonNode)new JsonObject{["id"]=s.Str("id"),["placeId"]=s.Str("id"),["name"]=s.Str("name"),["kind"]=s.Str("activityType")=="meal"?"meal":"attraction",["lat"]=s.Number("lat"),["lng"]=s.Number("lng"),["duration"]=s.Int("visitMinutes"),["cost"]=s.Number("cost"),["indoor"]=s.Bool("indoor"),["arrival"]=s.Str("arrival"),["departure"]=s.Str("departure"),["scheduledStart"]=Minute(s.Str("arrival")),["travelMinutes"]=d.Array("legs")![i]!.Int("minutes"),["hoursVerified"]=s.Bool("hoursVerified")}).ToArray());
        var travel=d.Array("legs")!.OfType<JsonObject>().Sum(l=>l.Int("minutes"));
        return new(){["date"]=d.Str("date"),["start"]=d["start"]!.DeepClone(),["end"]=d["end"]!.DeepClone(),["startMinute"]=profile.Int("startMinute"),["endMinute"]=profile.Int("endMinute"),["activities"]=activities,["transportMinutes"]=travel,["queueMinutes"]=0,["timeUsed"]=travel,["timeRemaining"]=1440-travel,["unknownQueue"]=activities.Count>0,["estimatedSteps"]=d.Int("estimatedSteps"),["estimatedCost"]=d.Number("estimatedCost"),["finishTime"]=d.Str("finishTime"),["stay"]=new JsonObject{["name"]=d.Object("end")!.Str("name"),["lat"]=d.Object("end")!.Number("lat"),["lng"]=d.Object("end")!.Number("lng"),["cost"]=0,["confirmed"]=true,["checkIn"]=900,["checkOut"]=660}};
    }
    static IEnumerable<int[]> Orders(int[] items) {
        if(items.Length==0){yield return [];yield break;}
        foreach(var item in items)foreach(var rest in Orders(items.Where(i=>i!=item).ToArray()))yield return new[]{item}.Concat(rest).ToArray();
    }
}
