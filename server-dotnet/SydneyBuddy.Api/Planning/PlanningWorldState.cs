using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// Uses the existing Meterless-style World Model projector; model prose cannot set route risk or totals.
public static class PlanningWorldState
{
    public static JsonObject Project(JsonObject trip,bool confirmed)
    {
        var evaluation=TravelWorldModelService.Evaluate(trip);
        evaluation.Object("summary")!["source"]=confirmed?"已確認行程的規則投影；不是實際支出或即時位置。":"未儲存草案的規則投影；不是實際支出或即時位置。";
        var events=new JsonArray(
            new JsonObject{["type"]="constraints_captured",["source"]="accepted_trip_metadata",["days"]=trip.Array("days")!.Count},
            new JsonObject{["type"]="route_projected",["source"]="deterministic_world_model",["stopCount"]=trip.Array("days")!.OfType<JsonObject>().Sum(d=>d.Array("stops")!.Count)},
            new JsonObject{["type"]=confirmed?"user_confirmed":"draft_proposed",["source"]=confirmed?"user_save_request":"planner",["tripRevision"]=trip.Int("revision")});
        return new(){["schemaVersion"]=1,["engine"]="Meterless World Model / application implementation",["phase"]=confirmed?"confirmed":"awaiting_confirmation",
            ["tripRevision"]=trip.Int("revision"),["summary"]=evaluation["summary"]!.DeepClone(),["suggestions"]=evaluation["suggestions"]!.DeepClone(),["events"]=events,["snapshots"]=evaluation["snapshots"]!.DeepClone()};
    }
}
