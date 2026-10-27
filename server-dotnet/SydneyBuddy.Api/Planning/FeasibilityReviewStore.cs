using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Private immutable review receipts. Confirmation saves the reviewed snapshot, never a client replacement.
public sealed class FeasibilityReviewStore(ProjectPaths paths)
{
    static readonly object Gate=new();
    string DirectoryPath=>Path.Combine(paths.DataDirectory,".feasibility-reviews");
    static string Digest(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Fingerprint(JsonNode node) {
        JsonNode? Canonical(JsonNode? value)=>value switch {JsonObject obj=>new JsonObject(obj.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,Canonical(p.Value)))),JsonArray array=>new JsonArray(array.Select(Canonical).ToArray()),_=>value?.DeepClone()};
        return Digest(Canonical(node)!.ToJsonString());
    }
    string PlanPath(string id)=>Path.Combine(DirectoryPath,"plan-"+Digest(id)+".json");
    public async Task<string> RegisterAsync(string id,int planRevision,int requirementsRevision,JsonObject trip,JsonObject profile,CancellationToken ct) {
        await ConfirmGate.WaitAsync(ct).ConfigureAwait(false);
        try { lock(Gate) {
            var hash=Fingerprint(new JsonObject{["trip"]=trip.DeepClone(),["requirements"]=profile.DeepClone()});var file=PlanPath(id);
            if(File.Exists(file)) {
                var prior=JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                if(planRevision<prior.Int("planRevision") || requirementsRevision<prior.Int("requirementsRevision") || planRevision==prior.Int("planRevision")&&requirementsRevision==prior.Int("requirementsRevision")&&hash!=prior.Str("fingerprint"))throw new RevisionConflictException();
            }
            Write(file,new(){["fingerprint"]=hash,["planRevision"]=planRevision,["requirementsRevision"]=requirementsRevision});return hash;
        }} finally {ConfirmGate.Release();}
    }
    public void Save(JsonObject review,JsonObject trip,JsonObject profile,string fingerprint) {
        lock(Gate)Write(ReviewPath(review.Str("reviewId")),new(){["review"]=review.DeepClone(),["trip"]=trip.DeepClone(),["requirements"]=profile.DeepClone(),["fingerprint"]=fingerprint});
    }
    string ReviewPath(string id) {if(!Guid.TryParseExact(id,"D",out _))throw new ServiceException("Invalid review identifier.");return Path.Combine(DirectoryPath,"review-"+id.ToLowerInvariant()+".json");}
    JsonObject Envelope(string id) {var p=ReviewPath(id);if(!File.Exists(p))throw new ServiceException("Review not found.");return JsonNode.Parse(File.ReadAllText(p))!.AsObject();}
    public JsonObject Read(string id) {
        lock(Gate) {
            var result=Envelope(id).Object("review")!.DeepClone().AsObject();
            if(result.Bool("canConfirm")) {
                try{Confirmable(id,result.Int("planRevision"),result.Int("requirementsRevision"));}
                catch(RevisionConflictException){result["canConfirm"]=false;result["stale"]=true;}
                catch(ServiceException){result["canConfirm"]=false;result["expired"]=true;}
            }
            return result;
        }
    }
    public JsonObject Confirmable(string id,int planRevision,int requirementsRevision) {
        lock(Gate) {
            var envelope=Envelope(id);var review=envelope.Object("review")!;
            if(!review.Bool("canConfirm") || !review.Bool("modelReviewCompleted") || review.Str("decision")!="pass")throw new ServiceException("The independent review has not passed.");
            if(planRevision!=review.Int("planRevision") || requirementsRevision!=review.Int("requirementsRevision"))throw new RevisionConflictException();
            var latest=JsonNode.Parse(File.ReadAllText(PlanPath(review.Str("planId"))))!.AsObject();
            if(latest.Str("fingerprint")!=envelope.Str("fingerprint") || latest.Int("planRevision")!=planRevision || latest.Int("requirementsRevision")!=requirementsRevision)throw new RevisionConflictException();
            if(!DateTimeOffset.TryParse(review.Str("validUntil"),out var until)||until<DateTimeOffset.UtcNow)throw new ServiceException("The review has expired. Please check this draft again.");
            return envelope.Object("trip")!.DeepClone().AsObject();
        }
    }
    public async Task<JsonObject> ConfirmAsync(string id,int planRevision,int requirementsRevision,int expected,TripRepository trips,CancellationToken ct) {
        // Lock only this review's confirmation flow. Register/Confirmable recheck plan state below.
        await ConfirmGate.WaitAsync(ct);
        try {
            JsonObject envelope;lock(Gate)envelope=Envelope(id);
            var review=envelope.Object("review")!;
            if(planRevision!=review.Int("planRevision")||requirementsRevision!=review.Int("requirementsRevision"))throw new RevisionConflictException();
            if(envelope.Object("confirmedTrip") is {} confirmed) {
                if(expected!=envelope.Int("confirmedFromRevision"))throw new RevisionConflictException();
                return confirmed.DeepClone().AsObject();
            }
            var trip=Confirmable(id,planRevision,requirementsRevision);
            if(expected!=trip.Int("revision",0))throw new RevisionConflictException();
            if(string.IsNullOrEmpty(trip.Str("id")))trip["id"]=id;
            TripContractValidator.Validate(trip);
            var saved=await trips.SaveAsync(trip,expected,ct).ConfigureAwait(false);
            lock(Gate){envelope["confirmedTrip"]=saved.DeepClone();envelope["confirmedFromRevision"]=expected;Write(ReviewPath(id),envelope);}
            return saved;
        }finally{ConfirmGate.Release();}
    }
    static readonly SemaphoreSlim ConfirmGate=new(1,1);
    void Write(string file,JsonObject value) {
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(DirectoryPath);else Directory.CreateDirectory(DirectoryPath,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write};if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;using(var stream=new FileStream(temporary,options))using(var writer=new StreamWriter(stream))writer.Write(value.ToJsonString());File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
