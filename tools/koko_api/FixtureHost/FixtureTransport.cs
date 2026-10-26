using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SydneyBuddy.Api;

namespace KokoApiFixture;

internal sealed record ScriptedResponse(string Message,string Kind,JsonNode? Payload,int Status,int DelayMs)
{
    public bool Used{get;set;}
}

/// <summary>Exact scripted transport responses only. It never extracts facts or chooses Koko's next question.</summary>
internal sealed class FixtureState(string mode)
{
    private readonly object gate=new();
    private string caseId="";
    private List<ScriptedResponse> responses=[];
    private readonly List<JsonObject> calls=[];
    public int TotalCalls{get{lock(gate)return calls.Count;}}
    public void Configure(JsonObject input)
    {
        var id=input.Str("caseId");
        if(id.Length is <1 or >120 || input.Array("responses") is not {} entries)throw new ServiceException("caseId and responses are required.");
        var parsed=new List<ScriptedResponse>();
        foreach(var entry in entries)
        {
            if(entry is not JsonObject value)throw new ServiceException("Each scripted response must be an object.");
            var message=value.Str("message").Trim();var kind=value.Str("kind","json");
            if(message.Length is <1 or >2000 || kind is not ("json" or "invalid_json" or "http_error" or "timeout" or "delay"))
                throw new ServiceException("Scripted response needs an exact message and supported kind.");
            if(kind is "json" or "delay" && value["payload"] is not JsonObject)throw new ServiceException("json/delay fixtures require an explicit payload object.");
            var status=value.Int("status",503);var delay=value.Int("delayMs",1000);
            if(status is <100 or >599 || delay is <0 or >110000)throw new ServiceException("Invalid fixture status/delay.");
            parsed.Add(new(message,kind,value["payload"]?.DeepClone(),status,delay));
        }
        lock(gate){caseId=id;responses=parsed;calls.Clear();}
    }
    public (ScriptedResponse? Script,JsonObject Call) Register(JsonObject payload,bool scripted)
    {
        var messages=payload.Array("messages") ?? new();
        var message=messages.LastOrDefault(x=>x.Str("role")=="user").Str("content");
        var system=messages.FirstOrDefault(x=>x.Str("role")=="system").Str("content");
        lock(gate)
        {
            var response=scripted ? responses.FirstOrDefault(x=>!x.Used && x.Message==message):null;
            if(response is not null)response.Used=true;
            var call=new JsonObject
            {
                ["callId"]=Guid.NewGuid().ToString("N"),["sequence"]=calls.Count+1,["caseId"]=caseId,["message"]=scripted ? message:"[synthetic live input omitted]",
                ["kind"]=scripted ? response?.Kind ?? "unexpected_message":"live",
                ["status"]=null,
                ["systemPromptSha256"]=FixtureProgram.Digest(system),["model"]=payload.Str("model"),
                ["timestampUtc"]=DateTimeOffset.UtcNow.ToString("O"),["matched"]=!scripted || response is not null
            };
            if(response?.Kind=="timeout")call["faultMechanism"]="synthetic_timeout_exception";
            if(response?.Kind=="delay")call["delayMs"]=response.DelayMs;
            calls.Add(call);
            return(response,(JsonObject)call.DeepClone());
        }
    }
    public void Complete(string callId,int? status,string outcome)
    {
        lock(gate)
        {
            var call=calls.FirstOrDefault(x=>x.Str("callId")==callId);
            if(call is null)return;
            call["status"]=status;call["outcome"]=outcome;
        }
    }
    public object Snapshot()
    {
        lock(gate)return new
        {
            caseId,mode,totalCalls=calls.Count,matchedCalls=calls.Count(x=>x.Bool("matched")),unexpectedCalls=calls.Count(x=>!x.Bool("matched")),
            unusedResponses=responses.Count(x=>!x.Used),calls=calls.Select(x=>x.DeepClone()).ToArray()
        };
    }
}

internal sealed class ScriptedNvidiaHandler(FixtureState state):HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if(request.RequestUri?.Host!="integrate.api.nvidia.com" || request.RequestUri.AbsolutePath!="/v1/chat/completions")
            throw new HttpRequestException("Unexpected fixture transport destination.");
        var payload=JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        var(script,call)=state.Register(payload,scripted:true);var callId=call.Str("callId");
        if(script is null){state.Complete(callId,null,"unmatched_fixture");throw new HttpRequestException("No exact scripted model response exists for this message.");}
        if(script.Kind=="timeout"){state.Complete(callId,null,"injected_timeout");throw new OperationCanceledException("Injected model timeout; not a real elapsed network deadline.");}
        if(script.Kind=="http_error"){state.Complete(callId,script.Status,"scripted_http_error");return new((HttpStatusCode)script.Status){Content=new StringContent("scripted model HTTP error")};}
        if(script.Kind=="delay")
        {
            try{await Task.Delay(script.DelayMs,cancellationToken);}
            catch(OperationCanceledException){state.Complete(callId,null,"delay_canceled");throw;}
        }
        var content=script.Kind=="invalid_json" ? (script.Payload is JsonValue value && value.TryGetValue<string>(out var invalid) ? invalid:"{not-valid-json") : script.Payload!.ToJsonString();
        var events=new StringBuilder();
        foreach(var chunk in content.EnumerateRunes().Chunk(32))
        {
            var text=string.Concat(chunk.Select(x=>x.ToString()));
            var envelope=new JsonObject{["choices"]=new JsonArray(new JsonObject{["delta"]=new JsonObject{["content"]=text}})};
            events.Append("data: ").Append(envelope.ToJsonString()).Append("\n\n");
        }
        events.Append("data: [DONE]\n\n");
        state.Complete(callId,200,script.Kind=="invalid_json" ? "scripted_invalid_json":"scripted_sse");
        return new(HttpStatusCode.OK){Content=new StringContent(events.ToString(),Encoding.UTF8,"text/event-stream")};
    }
}

/// <summary>Live mode records model/prompt metadata, never headers, keys, user payloads or response bodies.</summary>
internal sealed class ObservedLiveHandler(FixtureState state):DelegatingHandler(new HttpClientHandler{AllowAutoRedirect=false})
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        string? callId=null;
        if(request.Content is not null)
        {
            var payload=JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            callId=state.Register(payload,scripted:false).Call.Str("callId");
        }
        try
        {
            var response=await base.SendAsync(request,cancellationToken);
            if(callId is not null)state.Complete(callId,(int)response.StatusCode,"live_http_response");
            return response;
        }
        catch(Exception error)
        {
            if(callId is not null)state.Complete(callId,null,error is OperationCanceledException ? "live_canceled":"live_transport_error");
            throw;
        }
    }
}
