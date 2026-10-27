using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;
public static class FeasibilityEndpoints {
    public static void MapFeasibilityEndpoints(this RouteGroupBuilder routes) {
        routes.MapPost("/agents/feasibility/reviews",async(HttpContext context,FeasibilityAgent agent,TripRepository trips)=>{
            var input=await RequestJson.ReadAsync(context);
            if(input.Object("trip") is null && input.Str("tripId").Length>0) {
                var trip=await trips.FindAsync(input.Str("tripId"),context.RequestAborted)??throw new ServiceException("Trip not found.");
                if(input.ContainsKey("expectedSavedRevision") && input.Int("expectedSavedRevision",-1)!=trip.Int("revision"))throw new RevisionConflictException();
                input["trip"]=trip;
            }
            return Results.Json(await agent.ReviewAsync(input,context.RequestAborted));
        });
        routes.MapGet("/agents/feasibility/reviews/{id}",(string id,FeasibilityReviewStore store)=>Results.Json(store.Read(id)));
        routes.MapPost("/agents/feasibility/reviews/{id}/confirm",async(string id,HttpContext context,FeasibilityReviewStore reviews,TripRepository trips)=>{
            var input=await RequestJson.ReadAsync(context);
            if(input.ContainsKey("trip")||input.ContainsKey("canConfirm"))throw new ServiceException("Confirmation cannot replace the reviewed snapshot.");
            return Results.Json(await reviews.ConfirmAsync(id,input.Int("planRevision",-1),input.Int("requirementsRevision",-1),input.Int("expectedSavedRevision",-1),trips,context.RequestAborted));
        });
    }
}
