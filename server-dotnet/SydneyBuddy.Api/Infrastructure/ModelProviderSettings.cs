using System.Net.Http.Headers;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Explicit provider selection. Nightshift failures never silently fall back to direct NIM.
public sealed record ModelProviderSettings(string Provider,Uri Endpoint,string Key,string Model)
{
    public static ModelProviderSettings Resolve(IReadOnlyDictionary<string,string> config)
    {
        string Read(string name,string fallback="")=>config.GetValueOrDefault(name,fallback);
        var provider=Read("AI_PROVIDER","nvidia");if(string.IsNullOrWhiteSpace(provider))provider="nvidia";
        if(provider=="nightshift") {
            var url=Read("NIGHTSHIFT_BASE_URL","http://127.0.0.1:8088/v1");if(string.IsNullOrWhiteSpace(url))url="http://127.0.0.1:8088/v1";
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0 || !(uri.Scheme=="https" || uri.Scheme=="http" && (uri.IsLoopback)))throw new ServiceException("Nightshift requires HTTPS or a local loopback URL.");
            var model=Read("NIGHTSHIFT_MODEL","meterless/auto");if(string.IsNullOrWhiteSpace(model))model="meterless/auto";
            var key=Read("NIGHTSHIFT_API_KEY");if(string.IsNullOrWhiteSpace(key))throw new ServiceException("Nightshift is selected but its provider token is not configured. Your trip is unchanged.");
            return new("nightshift",new Uri(uri.AbsoluteUri.TrimEnd('/')+"/chat/completions"),key,model);
        }
        if(provider!="nvidia")throw new ServiceException("Unknown AI provider.");
        var nimKey=Read("NVIDIA_API_KEY");if(string.IsNullOrWhiteSpace(nimKey))throw new ServiceException("AI is not configured. Your trip details are kept.");
        var nimModel=Read("NVIDIA_MODEL");if(string.IsNullOrWhiteSpace(nimModel))nimModel=SecretStore.DefaultModel;
        return new("nvidia",new Uri("https://integrate.api.nvidia.com/v1/chat/completions"),nimKey,nimModel);
    }
    public static bool Configured(IReadOnlyDictionary<string,string> config) {try{Resolve(config);return true;}catch(ServiceException){return false;}}
    public JsonObject Prepare(JsonObject payload) {
        payload["model"]=Model;
        // Web Hotspot currently accepts text output only; the application still validates JSON itself.
        if(Provider=="nightshift"){payload.Remove("response_format");payload.Remove("chat_template_kwargs");}
        return payload;
    }
    public void Apply(HttpRequestMessage request,JsonObject payload) {
        request.RequestUri=Endpoint;request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Key);Prepare(payload);
    }
}
