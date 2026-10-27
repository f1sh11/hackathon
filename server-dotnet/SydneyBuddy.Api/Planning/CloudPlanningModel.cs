using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// Explicit, model-only opt-in. Does not enable Google or any other paid service.
public sealed class CloudPlanningModel(HttpClient client, SecretStore secrets, PlanningAgentSettings settings)
{
    public bool Configured => settings.CloudCallsAllowed && secrets.Read()["NVIDIA_API_KEY"].Length > 0;
    public async Task<JsonObject> CompleteAsync(string skill, JsonObject evidence, CancellationToken ct)
    {
        if (!settings.CloudCallsAllowed) throw new ServiceException("雲端行程模型尚未啟用，請在後端設定 PlanningAgent:AllowCloudModelCalls。");
        var config=secrets.Read();
        if(string.IsNullOrWhiteSpace(config["NVIDIA_API_KEY"]))throw new ServiceException("尚未設定 NVIDIA 行程模型金鑰，需求已保留；沒有改用示例行程。");
        var payload=new JsonObject {
            ["model"]=string.IsNullOrWhiteSpace(config["NVIDIA_MODEL"])?SecretStore.DefaultModel:config["NVIDIA_MODEL"],
            ["stream"]=false,["temperature"]=0.2,["max_tokens"]=4096,
            ["chat_template_kwargs"]=new JsonObject{["enable_thinking"]=false},
            ["response_format"]=new JsonObject{["type"]="json_object"},
            ["messages"]=new JsonArray(new JsonObject{["role"]="system",["content"]=skill},new JsonObject{["role"]="user",["content"]=evidence.ToJsonString()})
        };
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(180));
        try {
            using var request=new HttpRequestMessage(HttpMethod.Post,"https://integrate.api.nvidia.com/v1/chat/completions");
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",config["NVIDIA_API_KEY"]);
            request.Content=new StringContent(payload.ToJsonString(),Encoding.UTF8,"application/json");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(!response.IsSuccessStatusCode)throw new ServiceException($"雲端行程模型回傳 HTTP {(int)response.StatusCode}，請檢查後端金鑰、模型與額度；原行程保持不變。");
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
            using var body=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await stream.ReadAsync(buffer,deadline.Token))>0){
                if(body.Length+count>262144)throw new ServiceException("雲端行程模型回應過大，已拒絕結果。");
                body.Write(buffer,0,count);
            }
            return JsonNode.Parse(body.ToArray()) as JsonObject??throw new JsonException();
        }catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new ServiceException("雲端行程模型查詢逾時，需求已保留。");}
        catch(Exception e)when(e is HttpRequestException or IOException or JsonException or FormatException){throw new ServiceException("雲端行程模型連線失敗或回應無效，需求已保留。");}
    }
}
