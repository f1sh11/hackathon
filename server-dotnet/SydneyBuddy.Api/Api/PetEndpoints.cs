using System.Text.Json;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class PetEndpoints
{
    public static void MapPetEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/pet/sessions",async (HttpContext ctx,PetSessionStore store,MemoryRepository records,IPetModel model)=>
        {
            var input=await RequestJson.ReadAsync(ctx);var s=new PetSession();PetResponseLanguage.Apply(s,input);
            TravelProfiling.Refresh(s,await records.ListAsync(ctx.RequestAborted));
            if(input.Object("personalProfile") is JsonObject personal)s.PersonalProfile=(JsonObject)personal.DeepClone();
            if(input.Str("purpose")==PersonalProfileAgent.Purpose) {
                PersonalProfileAgent.Enable(s,input);
                var opening=await model.RespondAsync(s,"Begin this personal introduction. No answer has been given yet.",s.ActiveMemories,_=>Task.CompletedTask,ctx.RequestAborted);
                s.Messages.Add(PersonalProfileAgent.Opening(s,opening));
            } else {
                PetQuestions.Apply(s,input.Object("fields")??new());
                KokoDialogue.Enable(s,input);
                if(s.Dialogue is null)s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","嗨，我叫 Koko，是你的旅行向导。这次想过一段什么样的假期？随便说说就好。"));
            }
            await store.Gate.WaitAsync(ctx.RequestAborted);try{store.Save(s);return Results.Json(await PreparedView(s,store,ctx));}finally{store.Gate.Release();}
        });
        routes.MapGet("/pet/sessions/{id}",async(string id,HttpContext ctx,PetSessionStore store,MemoryRepository records,CancellationToken ct)=>
        {await store.Gate.WaitAsync(ct);try{var s=store.Load(id);TravelProfiling.Refresh(s,await records.ListAsync(ct));KokoDialogue.RefreshDeferredState(s);PetResponseLanguage.Apply(s,new JsonObject{["language"]=ctx.Request.Headers["X-App-Language"].ToString()});store.Save(s);return Results.Json(await PreparedView(s,store,ctx));}finally{store.Gate.Release();}});
        routes.MapDelete("/pet/sessions/{id}",async(string id,PetSessionStore store,IMemoryService memory,CancellationToken ct)=>
        {await store.Gate.WaitAsync(ct);try{await memory.RunAsync("discard_session",new JsonObject{["sessionId"]=id},ct);store.Delete(id);return Results.Json(new{deleted=true});}finally{store.Gate.Release();}});
        routes.MapPatch("/pet/sessions/{id}",async(string id,HttpContext ctx,PetSessionStore store)=>
        {
            var input=await RequestJson.ReadAsync(ctx);await store.Gate.WaitAsync(ctx.RequestAborted);
            try{var s=store.Load(id);PetResponseLanguage.Apply(s,input);
                if(PersonalProfileAgent.Enabled(s)) {
                    if(input.Object("personalProfile") is JsonObject updatedProfile)s.PersonalProfile=(JsonObject)updatedProfile.DeepClone();
                    if((input.Object("fields")?.Count ?? 0)>0 || input.Str("acceptedDirection").Length>0)throw new ServiceException("Trip details belong to the next step. This conversation only updates personal preferences.");
                    store.Save(s);return Results.Json(await PreparedView(s,store,ctx));
                }
                if(input.Str("acceptedDirection").Length>0){
                    if((input.Object("fields")?.Count ?? 0)>0)throw new ServiceException("Save trip details separately from choosing an idea.");
                    if(KokoDialogue.AcceptInspiration(s,input.Str("acceptedDirection")))store.Save(s);
                }else{PetQuestions.Apply(s,input.Object("fields")??new());KokoDialogue.Enable(s,input);KokoDialogue.DetailsUpdated(s,input.Object("fields")??new());store.Save(s);}
                return Results.Json(await PreparedView(s,store,ctx));}finally{store.Gate.Release();}
        });
        routes.MapPost("/pet/sessions/{id}/memory",async(string id,HttpContext ctx,PetSessionStore store,PreferenceService preferences,MemoryRepository records,IMemoryService memory)=>
        {
            var input=await RequestJson.ReadAsync(ctx);await store.Gate.WaitAsync(ctx.RequestAborted);
            try
            {
                var s=store.Load(id);PetResponseLanguage.Apply(s,input);
                if(input.Str("candidateId").Length>0) {
                    await DecidePreference(s,input,preferences,records,ctx.RequestAborted);
                    await SyncPending(s,memory,ctx.RequestAborted);store.Save(s);return Results.Json(await PreparedView(s,store,ctx));
                }
                var suggestion=s.PendingMemory;
                if(suggestion.Length==0) return Results.Json(await PreparedView(s,store,ctx));
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
                s.PendingMemory="";store.Save(s);return Results.Json(await PreparedView(s,store,ctx));
            }finally{store.Gate.Release();}
        });
        routes.MapPost("/pet/sessions/{id}/turn",Turn);
        routes.MapPost("/pet/sessions/{id}/profile/complete",async(string id,HttpContext ctx,PetSessionStore store,MemoryRepository records)=>
        {
            await store.Gate.WaitAsync(ctx.RequestAborted);
            try {
                var session=store.Load(id);PetResponseLanguage.Apply(session,new JsonObject{["language"]=ctx.Request.Headers["X-App-Language"].ToString()});TravelProfiling.Refresh(session,await records.ListAsync(ctx.RequestAborted));
                if(session.ConfirmedDna.Count==0)throw new ServiceException("先确认至少一项旅行偏好，再完成 Travel DNA。");
                if(session.Preferences.Any(x=>x.Status=="awaiting_confirmation"))throw new ServiceException("还有待确认的信息，可以记住、仅限这次或忽略后再完成。");
                session.DnaCompleted=true;store.Save(session);return Results.Json(await PreparedView(session,store,ctx));
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
            var s=store.Load(id);PetResponseLanguage.Apply(s,input);
            ctx.Response.ContentType="application/x-ndjson; charset=utf-8";
            ctx.Response.Headers.CacheControl="no-store";ctx.Response.Headers["X-Accel-Buffering"]="no";
            async Task Emit(object value){await ctx.Response.WriteAsync(JsonSerializer.Serialize(value,JsonExtensions.Options)+"\n",ctx.RequestAborted);await ctx.Response.Body.FlushAsync(ctx.RequestAborted);}
            if(s.CompletedRequests.Contains(requestId) || KokoDialogue.RepeatedChoice(s,input)){await Emit(new{type="done",session=await PreparedView(s,store,ctx)});return;}
            try
            {
                var choice=KokoDialogue.ResolveChoice(s,input);
                var field=input.Str("field");string reply;var mood="curious";JsonObject dialogueResult=new();
                if(field.Length>0)
                {
                    if(PersonalProfileAgent.Enabled(s))throw new ServiceException("Use a current choice or your own words to tell Koko about your usual preferences.");
                    if(!PetQuestions.Keys.Contains(field))throw new ServiceException("未知的旅行字段。");
                    var value=input.Str("value");if(value.Length is 0 or >600)throw new ServiceException("回答太长或为空。");
                    if(field=="date" && (value is "今天" or "明天" || value.Equals("Today",StringComparison.OrdinalIgnoreCase) || value.Equals("Tomorrow",StringComparison.OrdinalIgnoreCase)))value=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Sydney").Date.AddDays(value=="明天" || value.Equals("Tomorrow",StringComparison.OrdinalIgnoreCase) ? 1:0).ToString("yyyy-MM-dd");
                    // Changing currency never reuses an amount entered in a different currency.
                    if(field=="budgetCurrency" && s.Fields.TryGetValue(field,out var oldCurrency) && PetQuestions.Known(oldCurrency) && oldCurrency!=value)s.Fields.Remove("budget");
                    PetQuestions.Apply(s,new JsonObject{[field]=value});
                    if(field=="companions" && value is "一个人" or "两个人")s.Fields["travelers"]=value=="一个人"?"1":"2";
                    if(PetQuestions.Required.Contains(field) && !PetQuestions.Known(value)) {
                        var repeated=s.Messages.LastOrDefault()?.Mood=="pouting";
                        reply=repeated ? "没关系，我们慢慢来。我会留好背包，准备好再告诉我～\n"+PetQuestions.Question(field):PetQuestions.UncertainReply(field);mood=repeated ? "curious":"pouting";
                    } else reply=PetQuestions.UnsupportedDestination(s) ? "小爪爪先停一下～目前还不能生成这个城市的路线。可以改选悉尼，或者保留这份需求，之后再继续。": "收到啦～"+PetQuestions.Question(PetQuestions.Next(s));

                    if(s.Dialogue is not null)reply="这次的旅行资料已更新，可以继续调整草案。";
                }
                else
                {
                    var records=await memories.ListAsync(ctx.RequestAborted);
                    TravelProfiling.Refresh(s,records);
                    var personal=PersonalProfileAgent.Enabled(s);
                    var direct=personal ? null:KokoDialogue.DirectResponse(s,message,choice);
                    var confirmed=personal ? new JsonArray(records.Take(40).Select(x=>x?.DeepClone()).ToArray()):s.Dialogue is null ? (JsonArray)records.DeepClone():new JsonArray();
                    if(!personal && records.Count>0 && direct is null) {
                        try {
                            var recalled=await memory.RunAsync("query",new JsonObject{["query"]="travel preference "+(s.CurrentChoice?.QuestionText ?? "")+" "+message},ctx.RequestAborted);
                            var ids=(recalled.Array("memories") ?? new JsonArray()).Select(x=>x?.Object("memory").Str("id")).ToArray();
                            var relevant=s.Dialogue is null ? records.AsEnumerable():records.Where(x=>ids.Contains(x.Str("id")));
                            confirmed=new JsonArray(relevant.OrderBy(x=>{var i=Array.IndexOf(ids,x.Str("id"));return i<0 ? int.MaxValue:i;}).Take(s.Dialogue is null ? 40:8).Select(x=>x?.DeepClone()).ToArray());
                        } catch(ServiceException){s.MemoryWarning=s.Dialogue is null ? "长期偏好检索暂时不可用，本轮参考已确认的本机记录。":"长期偏好检索暂时不可用，先按这次对话继续准备。";}
                    }
                    var result=direct ?? await model.RespondAsync(s,message,confirmed,_=>Task.CompletedTask,ctx.RequestAborted);
                    if(personal){_=PersonalProfileAgent.Replies(result);PersonalProfileAgent.AvoidRepeatedQuestion(s,result);} // Validate before changing facts, staging memory or saving a turn.
                    dialogueResult=result;
                    mood=PetEmotion.Normalize(result.Str("mood"));
                    var changes=personal ? new JsonObject():result.Object("fields")??new();
                    // A reaction to a proposal cannot silently fill unrelated trip facts or global habits.
                    if(s.CurrentChoice is not null)changes=new();
                    else if(s.Dialogue is not null)PetExplicitFacts.FilterUnsupportedHabits(message,changes);
                    // Do not let model-filled unknown placeholders skip unanswered questions.
                    foreach(var key in PetQuestions.Keys)
                        if(changes.ContainsKey(key) && !PetQuestions.Known(changes.Str(key))) changes.Remove(key);
                    var explicitFacts=new JsonObject();if(!personal)PetExplicitFacts.Apply(message,explicitFacts);
                    foreach(var fact in explicitFacts)changes[fact.Key]=fact.Value?.DeepClone();
                    foreach(var uncertain in (!personal && s.CurrentChoice is null ? result.Array("needsClarification"):null) ?? new JsonArray()) {
                        var key=uncertain?.GetValue<string>() ?? "";
                        if(key.StartsWith("budget") && !PetExplicitFacts.MentionsBudget(message))continue;
                        if(PetQuestions.Required.Contains(key) && !explicitFacts.ContainsKey(key)){s.Fields.Remove(key);changes.Remove(key);}
                    }
                    if(changes.ContainsKey("budgetCurrency") && s.Fields.TryGetValue("budgetCurrency",out var previousCurrency) && PetQuestions.Known(previousCurrency) && previousCurrency!=changes.Str("budgetCurrency") && !changes.ContainsKey("budget"))s.Fields.Remove("budget");
                    PetQuestions.Apply(s,changes);KokoCompanion.ApplyFeeling(s,message);reply=result.Str("reply");
                    if(changes.ContainsKey("budget"))reply=PetExplicitFacts.BudgetReply(s) ?? "预算先收好啦，我们再确认金额、币种和范围。";
                    if(PetQuestions.Known(s.Fields.GetValueOrDefault("budgetCurrency")) && s.Fields["budgetCurrency"]!="AUD" && (changes.ContainsKey("budget") || changes.ContainsKey("budgetCurrency")))reply="这笔预算先按原币种保留。生成路线前还需要一份澳元活动预算。";
                    if(PetQuestions.UnsupportedDestination(s) && (changes.ContainsKey("destination") || message.Contains("生成") || reply.Contains("准备好"))){reply="可以先聊聊你想怎样玩，资料也会保留。目前能生成的路线是悉尼，其他城市还需要后续接入。";mood="confused";}
                    if(choice?.Intent is "skip" or "skip_name" or "use_alias" or "keep" or "draft" or "finish_profile")result.Remove("preferences");
                    var added=TravelProfiling.Apply(s,result,message,requestId);
                    if(added.Count>0) {
                        await SyncPending(s,memory,ctx.RequestAborted);
                        if(personal)PersonalProfileAgent.PresentCandidates(result,added);
                    }
                    var suggestion=s.Dialogue is not null || result.Array("preferences") is not null || !TravelProfiling.AllowLegacySuggestion(message) ? "":result.Str("memorySuggestion");if(suggestion.Length>0)s.PendingMemory=suggestion[..Math.Min(500,suggestion.Length)];
                }
                var assistant=PersonalProfileAgent.Enabled(s) ? PersonalProfileAgent.CompleteTurn(s,dialogueResult):KokoCompanion.RespondWithCare(s,KokoDialogue.CompleteTurn(s,dialogueResult,reply,mood),message);
                s.Messages.Add(new(requestId,"user",message,Choice:s.CurrentChoice));s.Messages.Add(assistant);
                if(s.Messages.Count>100)s.Messages=s.Messages.TakeLast(100).ToList();
                s.CompletedRequests.Add(requestId);if(s.CompletedRequests.Count>100)s.CompletedRequests.RemoveAt(0);
                store.Save(s);
                var view=await PreparedView(s,store,ctx);
                await Emit(new{type="reply",text=s.ResponseLanguage=="en" ? PetEnglishPresenter.Display(s,assistant.Text):assistant.Text});
                await Emit(new{type="done",session=view});
            }
            catch(ServiceException ex){await Emit(new{type="error",error=EnglishError(s,ex.Message)});}
            catch(Exception) when(!ctx.RequestAborted.IsCancellationRequested){await Emit(new{type="error",error=EnglishError(s,"保存或连接失败，请重新加载对话确认进度后重试。")});}
        }finally{store.Gate.Release();}
    }
    private static string EnglishError(PetSession session,string message) {
        if(session.ResponseLanguage!="en")return message;
        var known=PetEnglishCopy.Translate(message);
        return PetEnglishPresenter.HasChinese(known) ? "Something went wrong. Reload the conversation to check your progress, then try again.":known;
    }
    private static async Task<object> PreparedView(PetSession session,PetSessionStore store,HttpContext ctx) {
        var before=JsonSerializer.Serialize(session.EnglishDisplay);
        await ctx.RequestServices.GetRequiredService<PetEnglishPresenter>().PrepareAsync(session,ctx.RequestAborted);
        if(before!=JsonSerializer.Serialize(session.EnglishDisplay))store.Save(session);
        return PetQuestions.View(session);
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
            candidate.Status="trip_only";candidate.Scope="trip";return;
        }
        var current=await records.ListAsync(ct);TravelProfiling.Refresh(session,current);
        var alreadySaved=session.ConfirmedDna.FirstOrDefault(x=>x.Key==candidate.Key && x.Condition==candidate.Condition && x.Value==candidate.Value);
        if(alreadySaved is not null) {
            candidate.Status="confirmed";candidate.Scope="long_term";candidate.MemoryId=alreadySaved.MemoryId;
            return;
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

        TravelProfiling.Refresh(session,await records.ListAsync(ct));
        if(PersonalProfileAgent.Enabled(session))PersonalProfileAgent.Saved(session,candidate);
    }

}
