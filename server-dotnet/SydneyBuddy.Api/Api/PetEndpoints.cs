using System.Text.Json;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class PetEndpoints
{
    public static void MapPetEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/pet/sessions",async (HttpContext ctx,PetSessionStore store,MemoryRepository records)=>
        {
            var input=await RequestJson.ReadAsync(ctx);var s=new PetSession();
            TravelProfiling.Refresh(s,await records.ListAsync(ctx.RequestAborted));
            if(input.Object("personalProfile") is JsonObject personal)s.PersonalProfile=(JsonObject)personal.DeepClone();
            PetQuestions.Apply(s,input.Object("fields")??new());
            s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","嗨，我是 Buddy 🐨！这次想去哪儿，过一段什么样的小假期？随便说说就好，我帮你慢慢整理。"));
            await store.Gate.WaitAsync(ctx.RequestAborted);try{store.Save(s);}finally{store.Gate.Release();}
            return Results.Json(PetQuestions.View(s));
        });
        routes.MapGet("/pet/sessions/{id}",async(string id,PetSessionStore store,MemoryRepository records,CancellationToken ct)=>
        {await store.Gate.WaitAsync(ct);try{var s=store.Load(id);TravelProfiling.Refresh(s,await records.ListAsync(ct));store.Save(s);return Results.Json(PetQuestions.View(s));}finally{store.Gate.Release();}});
        routes.MapDelete("/pet/sessions/{id}",async(string id,PetSessionStore store,IMemoryService memory,CancellationToken ct)=>
        {await store.Gate.WaitAsync(ct);try{await memory.RunAsync("discard_session",new JsonObject{["sessionId"]=id},ct);store.Delete(id);return Results.Json(new{deleted=true});}finally{store.Gate.Release();}});
        routes.MapPatch("/pet/sessions/{id}",async(string id,HttpContext ctx,PetSessionStore store)=>
        {
            var input=await RequestJson.ReadAsync(ctx);await store.Gate.WaitAsync(ctx.RequestAborted);
            try{var s=store.Load(id);PetQuestions.Apply(s,input.Object("fields")??new());store.Save(s);return Results.Json(PetQuestions.View(s));}finally{store.Gate.Release();}
        });
        routes.MapPost("/pet/sessions/{id}/memory",async(string id,HttpContext ctx,PetSessionStore store,PreferenceService preferences,MemoryRepository records,IMemoryService memory)=>
        {
            var input=await RequestJson.ReadAsync(ctx);await store.Gate.WaitAsync(ctx.RequestAborted);
            try
            {
                var s=store.Load(id);
                if(input.Str("candidateId").Length>0) {
                    await DecidePreference(s,input,preferences,records,ctx.RequestAborted);
                    await SyncPending(s,memory,ctx.RequestAborted);store.Save(s);return Results.Json(PetQuestions.View(s));
                }
                var suggestion=s.PendingMemory;
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
        routes.MapPost("/pet/sessions/{id}/profile/complete",async(string id,HttpContext ctx,PetSessionStore store,MemoryRepository records)=>
        {
            await store.Gate.WaitAsync(ctx.RequestAborted);
            try {
                var session=store.Load(id);TravelProfiling.Refresh(session,await records.ListAsync(ctx.RequestAborted));
                if(session.ConfirmedDna.Count==0)throw new ServiceException("先确认至少一项旅行偏好，再完成 Travel DNA。");
                if(session.Preferences.Any(x=>x.Status=="awaiting_confirmation"))throw new ServiceException("还有待确认的信息，可以记住、仅限这次或忽略后再完成。");
                session.DnaCompleted=true;store.Save(session);return Results.Json(PetQuestions.View(session));
            }finally{store.Gate.Release();}
        });
    }
    private static async Task Turn(string id,HttpContext ctx,PetSessionStore store,IPetModel model,MemoryRepository memories,IMemoryService memory)
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
                else
                {
                    var records=await memories.ListAsync(ctx.RequestAborted);
                    TravelProfiling.Refresh(s,records);
                    var confirmed=(JsonArray)records.DeepClone();
                    if(records.Count>0) {
                        try {
                            var recalled=await memory.RunAsync("query",new JsonObject{["query"]="travel preference "+message},ctx.RequestAborted);
                            var ids=(recalled.Array("memories") ?? new JsonArray()).Select(x=>x?.Object("memory").Str("id")).ToArray();
                            confirmed=new JsonArray(records.OrderBy(x=>{var i=Array.IndexOf(ids,x.Str("id"));return i<0 ? int.MaxValue:i;}).Take(40).Select(x=>x?.DeepClone()).ToArray());
                        } catch(ServiceException){s.MemoryWarning="长期偏好检索暂时不可用，本轮参考已确认的本机记录。";}
                    }
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
                    if(PetQuestions.Known(s.Fields.GetValueOrDefault("budgetCurrency")) && s.Fields["budgetCurrency"]!="AUD" && (changes.ContainsKey("budget") || changes.ContainsKey("budgetCurrency")))reply="这笔预算先按原币种保留。生成路线前还需要一份澳元活动预算。";
                    if(PetQuestions.UnsupportedDestination(s) && (changes.ContainsKey("destination") || message.Contains("生成") || reply.Contains("准备好"))){reply="可以先聊聊你想怎样玩，资料也会保留。目前能生成的路线是悉尼，其他城市还需要后续接入。";mood="confused";}
                    var added=TravelProfiling.Apply(s,result,message,requestId);
                    if(added.Count>0)await SyncPending(s,memory,ctx.RequestAborted);
                    var suggestion=result.Array("preferences") is not null || !TravelProfiling.AllowLegacySuggestion(message) ? "":result.Str("memorySuggestion");if(suggestion.Length>0)s.PendingMemory=suggestion[..Math.Min(500,suggestion.Length)];
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
    private static async Task SyncPending(PetSession session,IMemoryService memory,CancellationToken ct)
    {
        try {
            await memory.RunAsync("stage",new JsonObject{["sessionId"]=session.Id,["candidates"]=JsonSerializer.SerializeToNode(session.Preferences.Where(x=>x.Status=="awaiting_confirmation"),JsonExtensions.Options)},ct);
            session.MemoryWarning="";
        } catch(ServiceException){session.MemoryWarning="候选偏好已保留在本次对话，H-MEM 暂未同步；确认保存时会重试。";}
    }
    private static async Task DecidePreference(PetSession session,JsonObject input,PreferenceService preferences,MemoryRepository records,CancellationToken ct)
    {
        var candidate=session.Preferences.FirstOrDefault(x=>x.Id==input.Str("candidateId"));
        if(candidate is null)throw new ServiceException("这条待确认信息已不存在，请刷新对话。");
        if(candidate.Status!="awaiting_confirmation")return; // repeated taps/retries are idempotent
        if(input.Bool("dismiss")){candidate.Status="rejected";return;}
        if(!input.Bool("consent")) {
            candidate.Status="trip_only";candidate.Scope="trip";TravelProfiling.AppendTripNote(session,candidate);return;
        }
        var current=await records.ListAsync(ct);TravelProfiling.Refresh(session,current);
        var alreadySaved=session.ConfirmedDna.FirstOrDefault(x=>x.Key==candidate.Key && x.Condition==candidate.Condition && x.Value==candidate.Value);
        if(alreadySaved is not null) {
            candidate.Status="confirmed";candidate.Scope="long_term";candidate.MemoryId=alreadySaved.MemoryId;
            TravelProfiling.AppendTripNote(session,candidate);return;
        }
        var expected=(input.Array("expectedConflicts") ?? new JsonArray()).Select(x=>x?.GetValue<string>() ?? "").Order().ToArray();
        if(!expected.SequenceEqual(candidate.ConflictingMemoryIds))throw new ServiceException("长期偏好刚有变化，请刷新对话，查看旧值后再确认替换。");
        if(candidate.ConflictingMemoryIds.Length>1)throw new ServiceException("这项偏好有多条旧记录，请先在记忆页整理，再确认更新。");
        var confirmed=JsonSerializer.SerializeToNode(candidate,JsonExtensions.Options)!.AsObject();
        confirmed["status"]="confirmed";confirmed["scope"]="long_term";
        var payload=new JsonObject{["consent"]=true,["summary"]=TravelProfiling.Label(candidate),["profile"]=new JsonObject{["travelDna"]=confirmed}};
        var saved=candidate.ConflictingMemoryIds.Length==1
            ? await preferences.UpdateAsync(candidate.ConflictingMemoryIds[0],payload,ct)
            : await preferences.SaveAsync(payload,ct);
        candidate.Status="confirmed";candidate.Scope="long_term";candidate.MemoryId=saved.Str("id");
        TravelProfiling.AppendTripNote(session,candidate);
        TravelProfiling.Refresh(session,await records.ListAsync(ct));
    }

}
