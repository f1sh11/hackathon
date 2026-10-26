using System.Text.Json;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class PetEndpoints
{
    public static void MapPetEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapPost("/pet/sessions",async (HttpContext ctx,PetSessionStore store,MemoryRepository records)=>
        {
            var input=await RequestJson.ReadAsync(ctx);var s=new PetSession();PetResponseLanguage.Apply(s,input);
            TravelProfiling.Refresh(s,await records.ListAsync(ctx.RequestAborted));
            if(input.Object("personalProfile") is JsonObject personal)s.PersonalProfile=(JsonObject)personal.DeepClone();
            PetQuestions.Apply(s,input.Object("fields")??new());
            KokoDialogue.Enable(s,input);
            if(s.Dialogue is null)s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","Hi, I’m Koko, your travel guide. What kind of getaway are you imagining? Tell me whatever comes to mind."));
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
            try{var s=store.Load(id);PetResponseLanguage.Apply(s,input);PetQuestions.Apply(s,input.Object("fields")??new());KokoDialogue.Enable(s,input);KokoDialogue.DetailsUpdated(s,input.Object("fields")??new());store.Save(s);return Results.Json(PetQuestions.View(s));}finally{store.Gate.Release();}
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
                    s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","Remembered: "+suggestion+". You can edit or remove this on the Memories page anytime."));
                }
                else
                {
                    s.Fields["notes"]=string.Join("；",new[]{s.Fields.GetValueOrDefault("notes",""),suggestion}.Where(x=>x.Length>0));
                    s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant","Sure, I’ll use that for this trip only."));
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
                if(session.ConfirmedDna.Count==0)throw new ServiceException("Confirm at least one travel preference before completing your Travel DNA.");
                if(session.Preferences.Any(x=>x.Status=="awaiting_confirmation"))throw new ServiceException("Some preferences still need a decision. Choose Remember, This trip only or Ignore, then finish.");
                session.DnaCompleted=true;store.Save(session);return Results.Json(PetQuestions.View(session));
            }finally{store.Gate.Release();}
        });
    }
    private static async Task Turn(string id,HttpContext ctx,PetSessionStore store,IPetModel model,MemoryRepository memories,IMemoryService memory)
    {
        var input=await RequestJson.ReadAsync(ctx);var message=input.Str("message").Trim();var requestId=input.Str("requestId");
        if(message.Length is 0 or >2000||!Guid.TryParse(requestId,out _))throw new ServiceException("Enter 1–2000 characters and use a valid request ID.");
        await store.Gate.WaitAsync(ctx.RequestAborted);
        try
        {
            var s=store.Load(id);PetResponseLanguage.Apply(s,input);
            ctx.Response.ContentType="application/x-ndjson; charset=utf-8";
            ctx.Response.Headers.CacheControl="no-store";ctx.Response.Headers["X-Accel-Buffering"]="no";
            async Task Emit(object value){await ctx.Response.WriteAsync(JsonSerializer.Serialize(value,JsonExtensions.Options)+"\n",ctx.RequestAborted);await ctx.Response.Body.FlushAsync(ctx.RequestAborted);}
            if(s.CompletedRequests.Contains(requestId)){await Emit(new{type="done",session=PetQuestions.View(s)});return;}
            try
            {
                var choice=KokoDialogue.ResolveChoice(s,input);
                var field=input.Str("field");string reply;var mood="curious";JsonObject dialogueResult=new();
                if(field.Length>0)
                {
                    if(!PetQuestions.Keys.Contains(field))throw new ServiceException("Unknown trip field.");
                    var value=input.Str("value");if(value.Length is 0 or >600)throw new ServiceException("The answer is empty or too long.");
                    if(field=="date"&&value is "今天" or "明天" or "Today" or "Tomorrow")value=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Sydney").Date.AddDays(value is "明天" or "Tomorrow" ? 1:0).ToString("yyyy-MM-dd");
                    // Changing currency never reuses an amount entered in a different currency.
                    if(field=="budgetCurrency" && s.Fields.TryGetValue(field,out var oldCurrency) && PetQuestions.Known(oldCurrency) && oldCurrency!=value)s.Fields.Remove("budget");
                    s.Fields[field]=value;
                    if(field=="companions" && value is "一个人" or "两个人" or "Solo" or "Two people")s.Fields["travelers"]=value is "一个人" or "Solo" ? "1":"2";
                    if(PetQuestions.Required.Contains(field) && !PetQuestions.Known(value)) {
                        var repeated=s.Messages.LastOrDefault()?.Mood=="pouting";
                        reply=repeated ? "No rush. Your progress is here whenever you’re ready.\n"+PetQuestions.Question(field):PetQuestions.UncertainReply(field);mood=repeated ? "curious":"pouting";
                    } else reply=PetQuestions.UnsupportedDestination(s) ? "I can’t generate routes for this city yet. You can choose Sydney or keep this draft for later.": "Noted. "+PetQuestions.Question(PetQuestions.Next(s));

                    if(s.Dialogue is not null)reply="Your trip details are updated. We can keep refining the draft.";
                }
                else
                {
                    var records=await memories.ListAsync(ctx.RequestAborted);
                    TravelProfiling.Refresh(s,records);
                    var direct=KokoDialogue.DirectResponse(s,message,choice);
                    var confirmed=s.Dialogue is null ? (JsonArray)records.DeepClone():new JsonArray();
                    if(records.Count>0 && direct is null) {
                        try {
                            var recalled=await memory.RunAsync("query",new JsonObject{["query"]="travel preference "+(s.CurrentChoice?.QuestionText ?? "")+" "+message},ctx.RequestAborted);
                            var ids=(recalled.Array("memories") ?? new JsonArray()).Select(x=>x?.Object("memory").Str("id")).ToArray();
                            var relevant=s.Dialogue is null ? records.AsEnumerable():records.Where(x=>ids.Contains(x.Str("id")));
                            confirmed=new JsonArray(relevant.OrderBy(x=>{var i=Array.IndexOf(ids,x.Str("id"));return i<0 ? int.MaxValue:i;}).Take(s.Dialogue is null ? 40:8).Select(x=>x?.DeepClone()).ToArray());
                        } catch(ServiceException){s.MemoryWarning=s.Dialogue is null ? "Long-term preference retrieval is temporarily unavailable. I’ll use the confirmed records stored locally for this turn.":"Long-term preference retrieval is temporarily unavailable. We can continue with this conversation.";}
                    }
                    var result=direct ?? await model.RespondAsync(s,message,confirmed,_=>Task.CompletedTask,ctx.RequestAborted);
                    dialogueResult=result;
                    mood=PetEmotion.Normalize(result.Str("mood"));
                    var changes=result.Object("fields")??new();
                    // A reaction to a proposal cannot silently fill unrelated trip facts or global habits.
                    if(s.CurrentChoice is not null)changes=new();
                    else if(s.Dialogue is not null)PetExplicitFacts.FilterUnsupportedHabits(message,changes);
                    // Do not let model-filled unknown placeholders skip unanswered questions.
                    foreach(var key in PetQuestions.Keys)
                        if(changes.ContainsKey(key) && !PetQuestions.Known(changes.Str(key))) changes.Remove(key);
                    var explicitFacts=new JsonObject();PetExplicitFacts.Apply(message,explicitFacts);
                    foreach(var fact in explicitFacts)changes[fact.Key]=fact.Value?.DeepClone();
                    foreach(var uncertain in (s.CurrentChoice is null ? result.Array("needsClarification"):null) ?? new JsonArray()) {
                        var key=uncertain?.GetValue<string>() ?? "";
                        if(key.StartsWith("budget") && !PetExplicitFacts.MentionsBudget(message))continue;
                        if(PetQuestions.Required.Contains(key) && !explicitFacts.ContainsKey(key)){s.Fields.Remove(key);changes.Remove(key);}
                    }
                    if(changes.ContainsKey("budgetCurrency") && s.Fields.TryGetValue("budgetCurrency",out var previousCurrency) && PetQuestions.Known(previousCurrency) && previousCurrency!=changes.Str("budgetCurrency") && !changes.ContainsKey("budget"))s.Fields.Remove("budget");
                    PetQuestions.Apply(s,changes);KokoCompanion.ApplyFeeling(s,message);reply=result.Str("reply");
                    if(changes.ContainsKey("budget"))reply=PetExplicitFacts.BudgetReply(s) ?? "I’ve noted the budget. Let’s confirm the amount, currency and scope.";
                    if(PetQuestions.Known(s.Fields.GetValueOrDefault("budgetCurrency")) && s.Fields["budgetCurrency"]!="AUD" && (changes.ContainsKey("budget") || changes.ContainsKey("budgetCurrency")))reply="I’ll keep this budget in its original currency. Before generating a route, we’ll also need an activity budget in AUD.";
                    if(PetQuestions.UnsupportedDestination(s) && (changes.ContainsKey("destination") || message.Contains("生成") || reply.Contains("准备好") || message.Contains("generate",StringComparison.OrdinalIgnoreCase) || reply.Contains("ready",StringComparison.OrdinalIgnoreCase))){reply="We can explore your ideas and keep the details. Route generation currently supports Sydney; other cities are still to come.";mood="confused";}
                    if(choice?.Intent is "skip" or "skip_name" or "use_alias" or "keep" or "draft")result.Remove("preferences");
                    var added=TravelProfiling.Apply(s,result,message,requestId);
                    if(added.Count>0)await SyncPending(s,memory,ctx.RequestAborted);
                    var suggestion=s.Dialogue is not null || result.Array("preferences") is not null || !TravelProfiling.AllowLegacySuggestion(message) ? "":result.Str("memorySuggestion");if(suggestion.Length>0)s.PendingMemory=suggestion[..Math.Min(500,suggestion.Length)];
                }
                var assistant=KokoCompanion.RespondWithCare(s,KokoDialogue.CompleteTurn(s,dialogueResult,reply,mood),message);
                await Emit(new{type="reply",text=assistant.Text});
                s.Messages.Add(new(requestId,"user",message,Choice:s.CurrentChoice));s.Messages.Add(assistant);
                if(s.Messages.Count>100)s.Messages=s.Messages.TakeLast(100).ToList();
                s.CompletedRequests.Add(requestId);if(s.CompletedRequests.Count>100)s.CompletedRequests.RemoveAt(0);
                store.Save(s);await Emit(new{type="done",session=PetQuestions.View(s)});
            }
            catch(ServiceException ex){await Emit(new{type="error",error=ex.Message});}
            catch(Exception) when(!ctx.RequestAborted.IsCancellationRequested){await Emit(new{type="error",error="Saving or connecting failed. Reload the conversation to check your progress, then retry."});}
        }finally{store.Gate.Release();}
    }
    private static async Task SyncPending(PetSession session,IMemoryService memory,CancellationToken ct)
    {
        try {
            await memory.RunAsync("stage",new JsonObject{["sessionId"]=session.Id,["candidates"]=JsonSerializer.SerializeToNode(session.Preferences.Where(x=>x.Status=="awaiting_confirmation"),JsonExtensions.Options)},ct);
            session.MemoryWarning="";
        } catch(ServiceException){session.MemoryWarning="Your preference candidates are kept in this conversation. H-MEM hasn’t synced yet; we’ll retry when you confirm saving.";}
    }
    private static async Task DecidePreference(PetSession session,JsonObject input,PreferenceService preferences,MemoryRepository records,CancellationToken ct)
    {
        var candidate=session.Preferences.FirstOrDefault(x=>x.Id==input.Str("candidateId"));
        if(candidate is null)throw new ServiceException("This preference candidate no longer exists. Refresh the conversation.");
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
        if(!expected.SequenceEqual(candidate.ConflictingMemoryIds))throw new ServiceException("Your saved preference has changed. Refresh the conversation and review the old value before confirming a replacement.");
        if(candidate.ConflictingMemoryIds.Length>1)throw new ServiceException("This preference has several previous records. Review them on the Memories page before confirming an update.");
        var confirmed=JsonSerializer.SerializeToNode(candidate,JsonExtensions.Options)!.AsObject();
        confirmed["status"]="confirmed";confirmed["scope"]="long_term";
        var payload=new JsonObject{["consent"]=true,["summary"]=TravelProfiling.Label(candidate),["profile"]=new JsonObject{["travelDna"]=confirmed}};
        var saved=candidate.ConflictingMemoryIds.Length==1
            ? await preferences.UpdateAsync(candidate.ConflictingMemoryIds[0],payload,ct)
            : await preferences.SaveAsync(payload,ct);
        candidate.Status="confirmed";candidate.Scope="long_term";candidate.MemoryId=saved.Str("id");

        TravelProfiling.Refresh(session,await records.ListAsync(ct));
    }

}
