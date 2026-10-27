using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// Run evidence contains codes, counts and provider names, never prompts, keys or personal facts.
public sealed class PlanningRunJournal(ProjectPaths paths, IHttpContextAccessor accessor, ILogger<PlanningRunJournal> logger)
{
    public const string Header = "X-Planning-Run";
    private static readonly object Gate = new();
    private const int MaximumBytes = 512_000;
    public string? CurrentId => accessor.HttpContext?.Items[typeof(PlanningRunJournal)] as string;
    public static bool Valid(string? id) => Guid.TryParseExact(id, "D", out _);
    public string Begin(HttpContext context)
    {
        var incoming = context.Request.Headers[Header].ToString();
        var id = Valid(incoming) ? incoming.ToLowerInvariant() : Guid.NewGuid().ToString();
        context.Items[typeof(PlanningRunJournal)] = id;
        context.Response.Headers[Header] = id;
        return id;
    }
    public void Record(string actor, string kind, string action, string status, JsonObject? evidence = null, int attempt = 0, string? target = null)
    {
        if (CurrentId is not {} id) return;
        var row = new JsonObject {
            ["run_id"]=id,["timestamp"]=DateTimeOffset.UtcNow.ToString("O"),["actor"]=actor,["actor_kind"]=kind,
            ["action"]=action,["status"]=status,["attempt"]=attempt,["target"]=target,["evidence"]=evidence?.DeepClone() ?? new JsonObject()
        };
        lock (Gate) {
            var directory=Path.Combine(paths.DataDirectory,".planning-runs");
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            var file=Path.Combine(directory,id+".jsonl");
            if(File.Exists(file) && new FileInfo(file).Length>=MaximumBytes) return;
            var options=new FileStreamOptions {Mode=FileMode.Append,Access=FileAccess.Write,Share=FileShare.Read};
            if(!OperatingSystem.IsWindows()) options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            // Count records under the same lock so parallel search requests have a strict sequence.
            row["sequence"]=File.Exists(file) ? File.ReadLines(file).Count()+1 : 1;
            var json=row.ToJsonString(JsonExtensions.Options);
            using(var stream=new FileStream(file,options)) using(var writer=new StreamWriter(stream)) writer.WriteLine(json);
            logger.LogInformation("PlanningEvent {PlanningEvent}",json);
        }
    }
    public JsonObject Read(string id)
    {
        if(!Valid(id)) throw new ServiceException("Invalid planning run identifier.");
        lock(Gate) {
            var file=Path.Combine(paths.DataDirectory,".planning-runs",id.ToLowerInvariant()+".jsonl");
            if(!File.Exists(file)) throw new ServiceException("Planning run not found.");
            if(new FileInfo(file).Length>MaximumBytes+20_000)throw new ServiceException("Planning run is too large to read.");
            return new JsonObject { ["runId"]=id,["events"]=new JsonArray(File.ReadLines(file).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>JsonNode.Parse(x)).ToArray()) };
        }
    }
}

public sealed class PlanningRunMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PlanningRunJournal journal)
    {
        var path=context.Request.Path.Value ?? "";
        if(path.StartsWith("/planning/runs/",StringComparison.Ordinal)) {await next(context);return;}
        var track=context.Request.Headers.ContainsKey(PlanningRunJournal.Header) || path is "/planning/test" or "/plan" or "/plan/prepare" or "/plan/remaining" or "/schedule/editor" || path.StartsWith("/pet/sessions/",StringComparison.Ordinal) || path.StartsWith("/agents/feasibility/",StringComparison.Ordinal);
        if(!track){await next(context);return;}
        journal.Begin(context);
        var route=path.StartsWith("/pet/sessions/",StringComparison.Ordinal)?"pet_turn":path switch {
            "/planning/test"=>"planning_test","/poi/search"=>"place_search","/plan/prepare"=>"prepare","/plan"=>"plan","/plan/remaining"=>"remaining","/schedule/editor"=>"schedule_check",_=>"related_request"};
        var started=Stopwatch.GetTimestamp();
        journal.Record("Orchestrator","application","request_started","running",new(){["operation"]=route});
        try {await next(context);journal.Record("Orchestrator","application","request_completed",context.Response.StatusCode<400?"success":"failed",new(){["operation"]=route,["http_status"]=context.Response.StatusCode,["elapsed_ms"]=(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds});}
        catch(Exception error) {
            journal.Record("Orchestrator","application","request_failed",context.RequestAborted.IsCancellationRequested?"cancelled":"failed",new(){["operation"]=route,["error_kind"]=error is ServiceException?"validation_or_provider":error is OperationCanceledException?"cancelled_or_timeout":"internal",["elapsed_ms"]=(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds});
            throw;
        }
    }
}
