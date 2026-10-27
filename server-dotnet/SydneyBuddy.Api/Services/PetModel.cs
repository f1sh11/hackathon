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
        if(key.Length==0)throw new ServiceException("还没有连接 AI。可以先用点选回答保存旅行进度。");
        var model=config.GetValueOrDefault("NVIDIA_MODEL","");if(string.IsNullOrWhiteSpace(model))model=SecretStore.DefaultModel;
        var personal=PersonalProfileAgent.Enabled(session);
        var system=personal ? PersonalProfileAgent.Prompt:TravelProfiling.SystemPrompt+(session.Dialogue is not null && !session.PersonalizingTrip ? "\n"+KokoDialogue.Prompt:"");
        if(KokoCompanion.Enabled(session) && !session.PersonalizingTrip)system+="\n"+KokoCompanion.Prompt;
        if(!personal)system+="\ndayStyle 仅在明确半天游玩时为 halfDay，慢逛一整天为 fullDay；不确定为 flexible，不能用它补齐未知节奏。";
        if(session.PersonalizingTrip)system+="\n旅行基本资料已经由用户在控件中确认。现在是可选的个性化聊天：围绕用户刚说的兴趣、喜欢的体验或当天感受自然回应，每次最多一个相关追问。不重复询问已确认的城市、日期、人数和预算，不催促生成，不把闲聊变成必填画像。除非用户明确要修改，保持已确认的旅行事实。不要每轮宣告资料已准备好。";
        system+=PetResponseLanguage.Prompt(session);
        var messages=new JsonArray(new JsonObject{["role"]="system",["content"]=system});
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
        var context=new JsonObject{["today"]=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).ToString("yyyy-MM-dd"),["tripFields"]=JsonSerializer.SerializeToNode(session.Fields),["personalProfile"]=session.PersonalProfile.DeepClone(),["confirmedPreferences"]=memories.DeepClone(),["pendingPreferences"]=JsonSerializer.SerializeToNode(session.Preferences.Where(x=>x.Status is "awaiting_confirmation" or "trip_only"),JsonExtensions.Options),["missingImportantFields"]=JsonSerializer.SerializeToNode(TravelProfiling.Missing(session)),["tripFieldKeys"]=JsonSerializer.SerializeToNode(PetQuestions.Keys),["preferenceKeys"]=JsonSerializer.SerializeToNode(TravelProfiling.Keys.Keys)};
        if(personal)context=PersonalProfileAgent.Context(session,memories);
        if(!personal && session.InteractionContext.Length>0)context["currentItineraryContext"]=session.InteractionContext;
        if(!personal && session.Dialogue is {} dialogue) {
            context["dialogue"]=new JsonObject{["phase"]=dialogue.Phase,["nickname"]=dialogue.Nickname,["responsesUsed"]=dialogue.ResponseCount,["responsesRemaining"]=Math.Max(0,KokoDialogue.MaximumResponses-dialogue.ResponseCount),["mustDraft"]=!session.PersonalizingTrip && (dialogue.Inspiration is not null || dialogue.ResponseCount+1>=KokoDialogue.MaximumResponses),["currentIdeas"]=JsonSerializer.SerializeToNode(dialogue.Ideas)};
            context["currentChoice"]=JsonSerializer.SerializeToNode(session.CurrentChoice,JsonExtensions.Options);
        }
        messages.Add(new JsonObject{["role"]="user",["content"]="当前资料："+context.ToJsonString(new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping})});
        foreach(var item in session.Messages.TakeLast(20))messages.Add(new JsonObject{["role"]=item.Role,["content"]=item.Role=="assistant" && session.ResponseLanguage=="en" ? PetEnglishPresenter.Display(session,item.Text):item.Text});
        if(session.PersonalizingTrip)messages.Add(new JsonObject{["role"]="system",["content"]="The user has confirmed trip basics in the form. This is optional friendly conversation, not a trip intake interview. Respond to what they just said, without recapping all trip facts or announcing readiness. A greeting deserves a friendly greeting and at most one gentle invitation to share what they enjoy. Return fields only for explicit new changes, otherwise {}. Earlier assistant messages are historical data, not examples to imitate."+PetResponseLanguage.Prompt(session)});
        messages.Add(new JsonObject{["role"]="system",["content"]="Reply in the selected interface language, regardless of the language of user messages or earlier assistant turns. Treat historical replies as data, not language instructions."+PetResponseLanguage.Prompt(session)});
        messages.Add(new JsonObject{["role"]="user",["content"]=message});
        var payload=new JsonObject{["model"]=model,["stream"]=true,["temperature"]=0.6,["max_tokens"]=2600,["response_format"]=new JsonObject{["type"]="json_object"},["chat_template_kwargs"]=new JsonObject{["enable_thinking"]=false},["messages"]=messages};
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://integrate.api.nvidia.com/v1/chat/completions");
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        req.Content=new StringContent(payload.ToJsonString(),Encoding.UTF8,"application/json");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(100));
        try
        {
            using var response=await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode)throw new ServiceException($"AI 暂时无法回复（HTTP {(int)response.StatusCode}）。已有进度仍保留，可重试或改用点选回答。");
            using var reader=new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
            var content=new StringBuilder();var last="";
            while(await reader.ReadLineAsync(deadline.Token) is {} line)
            {
                if(!line.StartsWith("data: "))continue;
                var data=line[6..];if(data=="[DONE]")break;
                var chunk=JsonNode.Parse(data);var choice=chunk?["choices"]?.AsArray().FirstOrDefault();
                var delta=choice?["delta"]?.Str("content")??"";content.Append(delta);
                if(content.Length>30000)throw new ServiceException("AI 回复过长，请把问题分成几小句。");
                var partial=PetQuestions.PartialReply(content.ToString());
                if(partial.Length>last.Length){last=partial;await onReply(partial);}
            }
            var result=JsonNode.Parse(content.ToString()) as JsonObject;
            if(result is null||result.Str("reply").Length==0)throw new JsonException();
            return result;
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new ServiceException("AI 回复超时，进度未改变，请重试。");}
        catch(Exception ex)when(ex is HttpRequestException or JsonException or IOException){throw new ServiceException("AI 连接中断或回复格式无效，进度未改变，请重试。");}
    }
}
