using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

public interface IPetModel
{
    Task<JsonObject> RespondAsync(PetSession session,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct);
}

// Dedicated NVIDIA-only channel. Does not enable Google/other paid services.
public sealed class PetModel(HttpClient client,SecretStore secrets):IPetModel
{
    public async Task<JsonObject> RespondAsync(PetSession session,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct)
    {
        var config=secrets.Read();var key=config.GetValueOrDefault("NVIDIA_API_KEY","");
        if(key.Length==0)throw new ServiceException("AI isn’t connected yet. You can use the choices to save your trip progress.");
        var model=config.GetValueOrDefault("NVIDIA_MODEL","");if(string.IsNullOrWhiteSpace(model))model=SecretStore.DefaultModel;
        var system=TravelProfiling.SystemPrompt+(session.Dialogue is not null ? "\n"+KokoDialogue.Prompt:"");
        if(KokoCompanion.Enabled(session))system+="\n"+KokoCompanion.Prompt;
        system+="\nUse dayStyle=halfDay only for an explicit half-day visit, fullDay for a relaxed whole day, and flexible when unknown. Do not use it to fill in an unknown pace.";
        system+=PetResponseLanguage.Prompt(session);
        var messages=new JsonArray(new JsonObject{["role"]="system",["content"]=system});
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
        var context=new JsonObject{["today"]=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).ToString("yyyy-MM-dd"),["tripFields"]=JsonSerializer.SerializeToNode(session.Fields),["personalProfile"]=session.PersonalProfile.DeepClone(),["confirmedPreferences"]=memories.DeepClone(),["pendingPreferences"]=JsonSerializer.SerializeToNode(session.Preferences.Where(x=>x.Status is "awaiting_confirmation" or "trip_only"),JsonExtensions.Options),["missingImportantFields"]=JsonSerializer.SerializeToNode(TravelProfiling.Missing(session)),["tripFieldKeys"]=JsonSerializer.SerializeToNode(PetQuestions.Keys),["preferenceKeys"]=JsonSerializer.SerializeToNode(TravelProfiling.Keys.Keys)};
        if(session.Dialogue is {} dialogue) {
            context["dialogue"]=new JsonObject{["phase"]=dialogue.Phase,["nickname"]=dialogue.Nickname,["responsesUsed"]=dialogue.ResponseCount,["responsesRemaining"]=Math.Max(0,KokoDialogue.MaximumResponses-dialogue.ResponseCount),["mustDraft"]=dialogue.Inspiration is not null || dialogue.ResponseCount+1>=KokoDialogue.MaximumResponses,["currentIdeas"]=JsonSerializer.SerializeToNode(dialogue.Ideas)};
            context["currentChoice"]=JsonSerializer.SerializeToNode(session.CurrentChoice,JsonExtensions.Options);
        }
        messages.Add(new JsonObject{["role"]="user",["content"]="Current context: "+context.ToJsonString()});
        foreach(var item in session.Messages.TakeLast(20))messages.Add(new JsonObject{["role"]=item.Role,["content"]=item.Text});
        messages.Add(new JsonObject{["role"]="user",["content"]=message});
        var payload=new JsonObject{["model"]=model,["stream"]=true,["temperature"]=0.6,["max_tokens"]=2600,["response_format"]=new JsonObject{["type"]="json_object"},["chat_template_kwargs"]=new JsonObject{["enable_thinking"]=false},["messages"]=messages};
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://integrate.api.nvidia.com/v1/chat/completions");
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        req.Content=new StringContent(payload.ToJsonString(),Encoding.UTF8,"application/json");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(100));
        try
        {
            using var response=await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode)throw new ServiceException($"AI is temporarily unavailable (HTTP {(int)response.StatusCode}). Your progress is saved; retry or use the choices.");
            using var reader=new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
            var content=new StringBuilder();var last="";
            while(await reader.ReadLineAsync(deadline.Token) is {} line)
            {
                if(!line.StartsWith("data: "))continue;
                var data=line[6..];if(data=="[DONE]")break;
                var chunk=JsonNode.Parse(data);var choice=chunk?["choices"]?.AsArray().FirstOrDefault();
                var delta=choice?["delta"]?.Str("content")??"";content.Append(delta);
                if(content.Length>30000)throw new ServiceException("The AI reply was too long. Please split your question into shorter parts.");
                var partial=PetQuestions.PartialReply(content.ToString());
                if(partial.Length>last.Length){last=partial;await onReply(partial);}
            }
            var result=JsonNode.Parse(content.ToString()) as JsonObject;
            if(result is null||result.Str("reply").Length==0)throw new JsonException();
            return result;
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new ServiceException("The AI reply timed out. Your progress is unchanged; please retry.");}
        catch(Exception ex)when(ex is HttpRequestException or JsonException or IOException){throw new ServiceException("The AI connection was interrupted or returned an invalid reply. Your progress is unchanged; please retry.");}
    }
}
