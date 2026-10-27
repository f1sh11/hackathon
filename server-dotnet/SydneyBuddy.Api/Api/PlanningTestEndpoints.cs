using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;

namespace SydneyBuddy.Api;

/// File contents are request data, never paths or executable instructions. No trip is saved here.
public static class PlanningTestEndpoints
{
    public const int MaxFileBytes=200_000;
    public static JsonObject Example() => new()
    {
        ["date"]=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Sydney").AddDays(1).ToString("yyyy-MM-dd"),
        ["days"]=1,["mode"]="demo",["language"]="zh-Hant",["budget"]=180,["maxSteps"]=10000,
        ["startMinute"]=600,["endMinute"]=1200,["interests"]=new JsonArray("文化","自然"),["mustVisit"]=new JsonArray(),
        ["transportPreference"]="walk",["energy"]="中精力",["accommodationStatus"]="unknown",
        ["start"]=new JsonObject{["name"]="Circular Quay",["lat"]=-33.8611,["lng"]=151.2126},
        ["end"]=new JsonObject{["name"]="Circular Quay",["lat"]=-33.8611,["lng"]=151.2126},
        ["request"]="測試行程規劃；景點不重複，準時用餐。這是 demo 測試，不代表即時班次、訂位或票價已驗證。"
    };

    public static void MapPlanningTestEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/planning/skills",(HttpContext context)=>{
            var language=context.Request.Query["language"].ToString();if(language.Length==0)language="zh-Hant";
            if(language is not ("en" or "zh-Hant"))throw new ServiceException("language 必須是 en 或 zh-Hant。");
            if(context.Request.Query["download"]=="true")return Results.Bytes(System.Text.Encoding.UTF8.GetBytes(ItinerarySkill.Read(language)),"text/markdown; charset=utf-8","itinerary."+language+".md");
            return Results.Json(new {version=ItinerarySkill.Version,baseVersion=ItinerarySkill.BaseVersion,source=ItinerarySkill.Source,sha256=ItinerarySkill.Hash(language),language,content=ItinerarySkill.Read(language)});
        });
        routes.MapGet("/planning/test/example",()=>Results.Bytes(JsonSerializer.SerializeToUtf8Bytes(Example(),JsonExtensions.Options),"application/json","planning-request.json"));
        routes.MapPost("/planning/test",async(HttpContext context,Planner planner,PaidServicePolicy paidServices,PlanningAgent agent)=>{
            var input=await ReadInputAsync(context);
            var started=Stopwatch.GetTimestamp();
            var draft=await PlanningEndpoints.CreateDraftAsync(input,planner,paidServices,agent,context.RequestAborted);
            var result=new JsonObject{["runId"]=context.Items[typeof(PlanningRunJournal)] as string,["saved"]=false,["skillVersion"]=ItinerarySkill.Version,
                ["elapsedMilliseconds"]=(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,["draft"]=draft};
            if(context.Request.Query["download"]=="true")return Results.Bytes(JsonSerializer.SerializeToUtf8Bytes(result,JsonExtensions.Options),"application/json","planning-result.json");
            return Results.Json(result);
        });
    }

    public static async Task<JsonObject> ReadInputAsync(HttpContext context)
    {
        if(context.Request.HasFormContentType){
            IFormCollection form;
            try { form=await context.Request.ReadFormAsync(new FormOptions{MultipartBodyLengthLimit=MaxFileBytes+8192,ValueLengthLimit=1024,ValueCountLimit=4},context.RequestAborted); }
            catch(InvalidDataException){throw new BadHttpRequestException("Invalid multipart upload",400);}
            if(form.Count!=0 || form.Files.Count!=1 || form.Files[0].Name!="file")throw new ServiceException("請上傳一個名為 file 的 JSON 檔案，不要附加其他欄位。");
            var file=form.Files[0];
            if(!file.FileName.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new ServiceException("只接受 .json 檔案。");
            if(file.Length>MaxFileBytes)throw new BadHttpRequestException("JSON file too large",413);
            await using var stream=file.OpenReadStream();return await ParseAsync(stream,context.RequestAborted);
        }
        if(!context.Request.HasJsonContentType())throw new BadHttpRequestException("Use application/json or multipart/form-data",415);
        return await ParseAsync(context.Request.Body,context.RequestAborted);
    }

    static async Task<JsonObject> ParseAsync(Stream stream,CancellationToken ct)
    {
        using var buffer=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await stream.ReadAsync(bytes,ct))>0){
            if(buffer.Length+count>MaxFileBytes)throw new BadHttpRequestException("JSON file too large",413);
            await buffer.WriteAsync(bytes.AsMemory(0,count),ct);
        }
        buffer.Position=0;
        return await JsonNode.ParseAsync(buffer,cancellationToken:ct) as JsonObject ?? throw new JsonException();
    }
}
