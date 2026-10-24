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
            if(input.Object("personalProfile") is JsonObject personal)s.PersonalProfile=(JsonObject)personal.DeepClone();
            PetQuestions.Apply(s,input.Object("fields")??new());
            s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","嗨，我是 Buddy 🐨！这次想去哪儿，过一段什么样的小假期？随便说说就好，我帮你慢慢整理。"));
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
                var field=input.Str("field");string reply;var mood="curious";
                if(field.Length>0)
                {
                    if(!PetQuestions.Keys.Contains(field))throw new ServiceException("未知的旅行字段。");
                    var value=input.Str("value");if(value.Length is 0 or >600)throw new ServiceException("回答太长或为空。");
                    if(field=="date"&&value is "今天" or "明天")value=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Sydney").Date.AddDays(value=="明天"?1:0).ToString("yyyy-MM-dd");
                    // Changing currency never reuses an amount entered in a different currency.
                    if(field=="budgetCurrency" && s.Fields.TryGetValue(field,out var oldCurrency) && PetQuestions.Known(oldCurrency) && oldCurrency!=value)s.Fields.Remove("budget");
                    s.Fields[field]=value;
                    if(field=="companions" && value is "一个人" or "两个人")s.Fields["travelers"]=value=="一个人"?"1":"2";
                    if(PetQuestions.Required.Contains(field) && !PetQuestions.Known(value)) {
                        var repeated=s.Messages.LastOrDefault()?.Mood=="pouting";
                        reply=repeated ? "没关系，我们慢慢来。我会留好背包，准备好再告诉我～\n"+PetQuestions.Question(field):PetQuestions.UncertainReply(field);mood=repeated ? "curious":"pouting";
                    } else reply=PetQuestions.UnsupportedDestination(s) ? "小爪爪先停一下～目前还不能生成这个城市的路线。可以改选悉尼，或者保留这份需求，之后再继续。": "收到啦～"+PetQuestions.Question(PetQuestions.Next(s));

                    await Emit(new{type="reply",text=reply});
                }
                else if(PetQuestions.Required.Contains(PetQuestions.Next(s)) && !PetQuestions.Known(message))
                {
                    reply=PetQuestions.UncertainReply(PetQuestions.Next(s));mood="pouting";
                    await Emit(new{type="reply",text=reply});
                }
                else
                {
                    var unanswered=PetQuestions.Next(s);
                    var records=await memories.ListAsync(ctx.RequestAborted);
                    var confirmed=new JsonArray(records.Select(x=>(JsonNode?)JsonValue.Create(x.Str("summary"))).ToArray());
                    var result=await model.RespondAsync(s,message,confirmed,_=>Task.CompletedTask,ctx.RequestAborted);
                    mood=PetEmotion.Normalize(result.Str("mood"));
                    var changes=result.Object("fields")??new();
                    // Do not let model-filled unknown placeholders skip unanswered questions.
                    foreach(var key in PetQuestions.Keys)
                        if(changes.Str(key) is "未确定" or "未知" or "未提供" or "unknown") changes.Remove(key);
                    PetExplicitFacts.Apply(message,changes);
                    foreach(var uncertain in result.Array("needsClarification") ?? new JsonArray()) {
                        var key=uncertain?.GetValue<string>() ?? "";
                        if(PetQuestions.Required.Contains(key)){s.Fields.Remove(key);changes.Remove(key);}
                    }
                    if(changes.ContainsKey("budgetCurrency") && s.Fields.TryGetValue("budgetCurrency",out var previousCurrency) && PetQuestions.Known(previousCurrency) && previousCurrency!=changes.Str("budgetCurrency") && !changes.ContainsKey("budget"))s.Fields.Remove("budget");
                    PetQuestions.Apply(s,changes);reply=result.Str("reply");
                    if(changes.ContainsKey("budget"))reply=PetExplicitFacts.BudgetReply(s) ?? "预算先收好啦，我们再确认金额、币种和范围。";
                    else if(reply.Length>220)reply="收到，你的想法已放进这次的小背包。";
                    if(PetQuestions.Next(s).Length==0)reply=(changes.ContainsKey("budget") ? (PetExplicitFacts.BudgetReply(s)??"")+"\n":"")+"小背包准备好啦！可以直接点下面的「生成行程」，可选信息以后再补也没关系～";
                    if(PetQuestions.Next(s).Length>0 && !reply.Contains('？') && !reply.Contains('?') &&
                        !new[]{"别问","不要问","不用问","再见","bye","don't ask","do not ask"}.Any(x=>message.Contains(x,StringComparison.OrdinalIgnoreCase)))
                        reply+="\n"+PetQuestions.Question(PetQuestions.Next(s));
                    if(PetQuestions.Required.Contains(unanswered) && !PetQuestions.Known(message) && !PetQuestions.Known(s.Fields.GetValueOrDefault(unanswered))) {reply=PetQuestions.UncertainReply(unanswered);mood="pouting";}
                    if(PetQuestions.Known(s.Fields.GetValueOrDefault("budgetCurrency")) && s.Fields["budgetCurrency"]!="AUD")reply="小钱包先等等～"+PetQuestions.Question("budgetCurrency");
                    if(PetQuestions.UnsupportedDestination(s)){reply="小爪爪先停一下～目前还不能生成这个城市的路线。可以改选悉尼，或者保留这份需求，之后再继续。";mood="confused";}
                    var suggestion=new[]{"以后","平时","长期","每次","一直","记住"}.Any(message.Contains) && !new[]{"不保存","不要记","别记","不记住","只限这次","仅这次","只用于这次"}.Any(message.Contains) ? result.Str("memorySuggestion"):"";if(suggestion.Length>0)s.PendingMemory=suggestion[..Math.Min(500,suggestion.Length)];
                    await Emit(new{type="reply",text=reply});
                }
                s.Messages.Add(new(requestId,"user",message));s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",reply,mood));
                if(s.Messages.Count>100)s.Messages=s.Messages.TakeLast(100).ToList();
                s.CompletedRequests.Add(requestId);if(s.CompletedRequests.Count>100)s.CompletedRequests.RemoveAt(0);
                store.Save(s);await Emit(new{type="done",session=PetQuestions.View(s)});
            }
            catch(ServiceException ex){await Emit(new{type="error",error=ex.Message});}
            catch(Exception) when(!ctx.RequestAborted.IsCancellationRequested){await Emit(new{type="error",error="保存或连接失败，请重新加载对话确认进度后重试。"});}
        }finally{store.Gate.Release();}
    }
}
