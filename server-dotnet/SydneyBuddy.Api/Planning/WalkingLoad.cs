using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Planning assumptions, not pedometer observations or a medical fitness assessment.
public static class WalkingLoad
{
    public const string Version = "2026-10-27.2";
    public static double Speed(JsonObject profile)=>profile.Number("walkingSpeedKmh",4.5);
    public static double Stride(JsonObject profile)=>profile.Number("stepLengthMeters",0.75);
    public static double Cadence(JsonObject profile)=>Speed(profile)*1000/60/Stride(profile);
    public static int DistanceSteps(double meters,JsonObject profile)=>(int)Math.Ceiling(Math.Max(0,meters)/Stride(profile));
    public static double Minutes(JsonObject leg,JsonObject profile)=>leg.Number("walkingMeters",leg.Number("meters"))*60/(Speed(profile)*1000);
    public static double VisitMinutes(JsonObject place,int duration,string? kind=null)
    {
        if(RouteLogistics.IsTask(place)||RecommendationQuality.IsMeal(place)||kind is "meal" or "rest" or "restroom" or "stay" or "checkout" or "bag-drop" or "bag-pickup")return 0;
        // Explicit source evidence wins. Otherwise show a declared activity-type assumption.
        if(place["walkingMinutes"] is not null)return Math.Clamp(place.Number("walkingMinutes"),0,duration);
        var fraction=place.Number("walkingFraction",double.NaN);
        if(!double.IsFinite(fraction))fraction=place.Str("poiKind") is "hiking_trail" or "walking_tour" or "trail" ? 1 :
            place["indoorKnown"] is not null&&!place.Bool("indoorKnown") ? .35 : place.Bool("indoor") ? .3 : .6;
        return duration*Math.Clamp(fraction,0,1);
    }
    public static int VisitSteps(JsonObject point,int duration,JsonObject profile,string? kind=null)=>(int)Math.Ceiling(VisitMinutes(point,duration,kind)*Cadence(profile));
    public static JsonObject Breakdown(JsonObject profile,JsonArray stops,JsonArray legs,int total,int previouslyCompletedSteps=0)
    {
        var travel=legs.OfType<JsonObject>().Sum(l=>DistanceSteps(l.Number("walkingMeters",l.Number("meters")),profile));
        var active=stops.OfType<JsonObject>().Sum(s=>s.Number("activityWalkingMinutes"));
        var continuous=legs.OfType<JsonObject>().Sum(l=>Minutes(l,profile))+stops.OfType<JsonObject>().Sum(s=>s.Int("visitMinutes"));
        return new(){["version"]=Version,["status"]="estimate",["walkingSpeedKmh"]=Speed(profile),["stepLengthMeters"]=Stride(profile),["stepsPerMinute"]=Cadence(profile),
            ["routeSteps"]=travel,["activitySteps"]=Math.Max(0,total-travel-previouslyCompletedSteps),["previouslyCompletedSteps"]=previouslyCompletedSteps,["activityWalkingMinutes"]=Math.Round(active,1),["estimatedSteps"]=total,
            ["continuousWalkingMinutes"]=Math.Round(continuous,1),["continuousWalkingSteps"]=(int)Math.Ceiling(continuous*Cadence(profile)),
            ["basis"]="Route distance / step length, plus activity walking minutes × speed / step length. Activity fractions are planning assumptions. Continuous walking is a hypothetical scenario for all route-walking and visit time; seated meals and rest are excluded from estimatedSteps. Transit rides, waiting and buffers are not walking. Not measured steps."};
    }
}
