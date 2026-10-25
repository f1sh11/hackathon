using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;
public sealed class TravelWorldModelTests {
    private static JsonObject Trip() => JsonNode.Parse("""
    {"profile":{"maxSteps":10000,"budget":100,"startMinute":600},"weather":{"status":"partial","days":[{"date":"2026-10-25","available":true,"rainy":false},{"date":"2026-10-26","available":false,"rainy":false}]},"days":[
    {"date":"2026-10-25","planningStartMinute":660,"estimatedSteps":6000,"estimatedCost":60,"start":{"name":"起点"},"end":{"name":"酒店"},"finishTime":"17:00","stops":[{"name":"A","arrival":"12:00","cost":60,"steps":1200,"indoor":true,"visitMinutes":60}],"legs":[{"minutes":40,"source":"Apple MapKit"},{"minutes":40,"source":"TfNSW"}]},
    {"date":"2026-10-26","estimatedSteps":6000,"estimatedCost":60,"start":{"name":"酒店"},"end":{"name":"酒店"},"finishTime":"17:00","stops":[{"name":"B","arrival":"12:00","cost":60,"steps":2400,"indoor":false,"visitMinutes":60}],"legs":[{"minutes":40,"source":"Apple MapKit"},{"minutes":40,"source":"Apple MapKit"}]}]}
    """)!.AsObject();
    [Fact] public void MultiDayChecksUseDailyLimitsAndActualStopStepsAndDeparture() {
        var result=TravelWorldModelService.Evaluate(Trip());var summary=result.Object("summary")!;
        Assert.Equal(12000,summary.Int("walkingSteps"));Assert.Equal("low",summary.Str("energyRisk"));Assert.Equal("low",summary.Str("budgetRisk"));
        Assert.DoesNotContain(result.Array("suggestions")!.OfType<JsonObject>(),x=>x.Str("id").StartsWith("transport") || x.Str("id").StartsWith("energy"));
        Assert.Equal("11:00",result.Array("snapshots")![0]!.Str("time"));Assert.Equal(1200,result.Array("snapshots")![1]!.Int("steps"));Assert.Null(summary["after"]);
    }
    [Fact] public void MissingForecastAndUnknownPriceAreNotClassifiedAsSafe() {
        var trip=Trip();trip.Array("days")![0]!.Array("stops")![0]!["costKnown"]=false;
        var summary=TravelWorldModelService.Evaluate(trip).Object("summary")!;
        Assert.Equal("unknown",summary.Str("weatherRisk"));Assert.Equal("unknown",summary.Str("budgetRisk"));
        Assert.Equal("low",summary.Array("days")![0]!.Str("weatherRisk"));Assert.Equal("unknown",summary.Array("days")![1]!.Str("weatherRisk"));
    }
    [Fact] public void ExplicitDailyTravelLimitsOverrideOriginalProfile() {
        var trip=Trip();trip["schedule"]=JsonNode.Parse("""
        {"settings":{"maxSteps":15000,"dailyBudget":150},"days":[{"date":"2026-10-26","maxSteps":5000,"dailyBudget":50,"startMinute":720}]}
        """);
        var result=TravelWorldModelService.Evaluate(trip);var days=result.Object("summary")!.Array("days")!;
        Assert.Equal("low",days[0]!.Str("energyRisk"));Assert.Equal("high",days[1]!.Str("energyRisk"));Assert.Equal("high",days[1]!.Str("budgetRisk"));
        Assert.Equal("12:00",result.Array("snapshots")!.OfType<JsonObject>().Single(x=>x.Int("day")==2 && x.Str("eventType")=="day_started").Str("time"));
    }
    [Fact] public void ChineseEstimateLabelsCannotBeMistakenForRoadEvidence() {
        var trip=Trip();trip.Array("days")![0]!.Array("legs")![0]!["source"]="初筛估算";
        var result=TravelWorldModelService.Evaluate(trip);
        Assert.False(result.Object("summary")!.Array("days")![0]!.Bool("routeKnown"));
        Assert.Contains(result.Array("suggestions")!.OfType<JsonObject>(),x=>x.Str("id")=="verify-0" && x.Str("detail").Contains("交通为估算"));
    }
    [Fact] public void FindingsIdentifyOnlyAffectedDayAndDoNotChangeItinerary() {
        var trip=Trip();trip.Array("days")![1]!["estimatedSteps"]=12000;trip.Array("days")![1]!["estimatedCost"]=140;
        var source=trip.ToJsonString();var result=TravelWorldModelService.Evaluate(trip);
        Assert.Equal(source,trip.ToJsonString());Assert.Equal("high",result.Object("summary")!.Str("energyRisk"));
        var energy=Assert.Single(result.Array("suggestions")!.OfType<JsonObject>(),x=>x.Str("id").StartsWith("energy"));Assert.Equal(1,energy.Int("dayIndex"));
    }
}
