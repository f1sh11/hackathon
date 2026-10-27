using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Model proposals are untrusted. The evaluator is explicitly deterministic, not another claimed LLM.
/// At most two repair turns; fallback is the existing bounded optimizer with identical hard constraints.
public static class PlanningCollaboration
{
    public static async Task<JsonArray> RunAsync(Func<JsonObject?,Task<JsonObject>> propose,
        Func<JsonObject,Task<(JsonArray? Days,JsonArray Issues)>> evaluate,Func<Task<JsonArray>> fallback,
        PlanningRunJournal? journal,CancellationToken ct)
    {
        JsonObject? feedback=null;
        for(var attempt=0;attempt<3;attempt++) {
            ct.ThrowIfCancellationRequested();
            journal?.Record("PlanningAgent","model",attempt==0?"propose_itinerary":"revise_itinerary","running",attempt:attempt,target:"RouteEvaluator");
            var proposal=await propose(feedback);
            var (days,issues)=await evaluate(proposal);
            if(days is not null) {
                journal?.Record("RouteEvaluator","deterministic","validate_proposal","approved",new(){["days"]=days.Count},attempt,"PlanningAgent");
                return days;
            }
            journal?.Record("RouteEvaluator","deterministic","validate_proposal","needs_revision",new(){["issues"]=issues.DeepClone()},attempt,"PlanningAgent");
            // A provider that cannot supply a structured proposal is never described as an approved agent.
            if(issues.OfType<JsonObject>().Any(x=>x.Str("code")=="proposal_missing"))break;
            feedback=new(){["issues"]=issues.DeepClone(),["instruction"]="Revise only candidate choices, order and day allocation. Never relax the user's constraints. Use only supplied candidates and verified day candidate sets."};
        }
        journal?.Record("RouteOptimizer","deterministic","bounded_fallback","running",target:"RouteEvaluator");
        try {
            var result=await fallback();
            journal?.Record("RouteEvaluator","deterministic","bounded_fallback","approved",new(){["days"]=result.Count});
            return result;
        }catch(ServiceException) {
            journal?.Record("RouteEvaluator","deterministic","bounded_fallback","blocked",new(){["code"]="constraints_unsatisfied",["constraints_relaxed"]=false});
            throw;
        }
    }
}
