using System.Text.Json;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class PetEndpoints
{
    public static void MapPetEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/pet/sessions",async (HttpContext ctx,PetSessionStore store)=>
        {
            var input=await RequestJson.ReadAsync(ctx);var s=new PetSession();
            PetQuestions.Apply(s,input.Object("fields")??new());
            s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","嗨，我是 Buddy 🐨！我会保存这次旅行的进度，方便你回来继续。长期喜好会先问过你。"+PetQuestions.Question(PetQuestions.Next(s))));
            await store.Gate.WaitAsync(ctx.RequestAborted);try{store.Save(s);}finally{store.Gate.Release();}
            return Results.Json(PetQuestions.View(s));
        });
        routes.MapGet("/pet/sessions/{id}",async(string id,PetSessionStore store,CancellationToken ct)=>
        {await store.Gate.WaitAsync(ct);try{return Results.Json(PetQuestions.View(store.Load(id)));}finally{store.Gate.Release();}});
        routes.MapDelete("/pet/sessions/{id}",async(string id,PetSessionStore store,CancellationToken ct)=>
        {await store.Gate.WaitAsync(ct);try{store.Delete(id);return Results.Json(new{deleted=true});}finally{store.Gate.Release();}});
        routes.MapPatch("/pet/sessions/{id}",async(string id,HttpContext ctx,PetSessionStore store)=>
        {
            var input=await RequestJson.ReadAsync(ctx);await store.Gate.WaitAsync(ctx.RequestAborted);
            try{var s=store.Load(id);PetQuestions.Apply(s,input.Object("fields")??new());store.Save(s);return Results.Json(PetQuestions.View(s));}finally{store.Gate.Release();}
        });
        routes.MapPost("/pet/sessions/{id}/memory",async(string id,HttpContext ctx,PetSessionStore store,PreferenceService preferences)=>
        {
            var input=await RequestJson.ReadAsync(ctx);await store.Gate.WaitAsync(ctx.RequestAborted);
            try
            {
                var s=store.Load(id);var suggestion=s.PendingMemory;
                if(suggestion.Length==0) return Results.Json(PetQuestions.View(s));
                if(input.Bool("consent"))
                {
                    await preferences.SaveAsync(new JsonObject{["consent"]=true,["summary"]=suggestion,["profile"]=new JsonObject()},ctx.RequestAborted);
                    s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","记好啦："+suggestion+"。随时可以在记忆页修改或撤回。"));
                }
                else
                {
                    s.Fields["notes"]=string.Join("；",new[]{s.Fields.GetValueOrDefault("notes",""),suggestion}.Where(x=>x.Length>0));
                    s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","好呀，只放进这次旅行的小背包。"));
                }
                s.PendingMemory="";store.Save(s);return Results.Json(PetQuestions.View(s));
            }finally{store.Gate.Release();}
        });
        routes.MapPost("/pet/sessions/{id}/turn",Turn);
    }
    private static async Task Turn(string id,HttpContext ctx,PetSessionStore store,IPetModel model,MemoryRepository memories)
    {
        var input=await RequestJson.ReadAsync(ctx);var message=input.Str("message").Trim();var requestId=input.Str("requestId");
        if(message.Length is 0 or >2000||!Guid.TryParse(requestId,out _))throw new ServiceException("请输入 1–2000 字，并使用有效的请求编号。");
        await store.Gate.WaitAsync(ctx.RequestAborted);
        try
        {
            var s=store.Load(id);
            ctx.Response.ContentType="application/x-ndjson; charset=utf-8";
            ctx.Response.Headers.CacheControl="no-store";ctx.Response.Headers["X-Accel-Buffering"]="no";
            async Task Emit(object value){await ctx.Response.WriteAsync(JsonSerializer.Serialize(value,JsonExtensions.Options)+"\n",ctx.RequestAborted);await ctx.Response.Body.FlushAsync(ctx.RequestAborted);}
            if(s.CompletedRequests.Contains(requestId)){await Emit(new{type="done",session=PetQuestions.View(s)});return;}
            try
            {
                var field=input.Str("field");string reply;
                if(field.Length>0)
                {
                    if(!PetQuestions.Keys.Contains(field))throw new ServiceException("未知的旅行字段。");
                    var value=input.Str("value");if(value.Length is 0 or >600)throw new ServiceException("回答太长或为空。");
                    if(field=="date"&&value is "今天" or "明天")value=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Sydney").Date.AddDays(value=="明天"?1:0).ToString("yyyy-MM-dd");
                    s.Fields[field]=value;reply="收到啦～"+PetQuestions.Question(PetQuestions.Next(s));
                    if(field=="interests"&&value!="未确定")s.PendingMemory="喜欢"+value;
                    await Emit(new{type="reply",text=reply});
                }
                else
                {
                    var records=await memories.ListAsync(ctx.RequestAborted);
                    var confirmed=new JsonArray(records.Select(x=>(JsonNode?)JsonValue.Create(x.Str("summary"))).ToArray());
                    var result=await model.RespondAsync(s,message,confirmed,text=>Emit(new{type="reply",text}),ctx.RequestAborted);
                    var changes=result.Object("fields")??new();
                    // Do not let model-filled unknown placeholders skip unanswered questions.
                    foreach(var key in PetQuestions.Keys)
                        if(changes.Str(key) is "未确定" or "未知" or "未提供" or "unknown") changes.Remove(key);
                    PetQuestions.Apply(s,changes);reply=result.Str("reply");
                    if(PetQuestions.Next(s).Length>0 && !reply.Contains('？') && !reply.Contains('?') &&
                        !new[]{"别问","不要问","不用问","再见","bye","don't ask","do not ask"}.Any(x=>message.Contains(x,StringComparison.OrdinalIgnoreCase)))
                        reply+="\n"+PetQuestions.Question(PetQuestions.Next(s));
                    var suggestion=result.Str("memorySuggestion");if(suggestion.Length>0)s.PendingMemory=suggestion[..Math.Min(500,suggestion.Length)];
                }
                s.Messages.Add(new(requestId,"user",message));s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",reply));
                if(s.Messages.Count>100)s.Messages=s.Messages.TakeLast(100).ToList();
                s.CompletedRequests.Add(requestId);if(s.CompletedRequests.Count>100)s.CompletedRequests.RemoveAt(0);
                store.Save(s);await Emit(new{type="done",session=PetQuestions.View(s)});
            }
            catch(ServiceException ex){await Emit(new{type="error",error=ex.Message});}
            catch(Exception) when(!ctx.RequestAborted.IsCancellationRequested){await Emit(new{type="error",error="保存或连接失败，请重新加载对话确认进度后重试。"});}
        }finally{store.Gate.Release();}
    }
}
