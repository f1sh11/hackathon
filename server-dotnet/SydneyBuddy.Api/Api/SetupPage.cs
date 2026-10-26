using System.Net;

namespace SydneyBuddy.Api;

/// <summary>渲染仅包含配置状态的Local setup页。 / Renders local configuration status, never secret values.</summary>
public static class SetupPage
{
    public static string Render(SecretStore secrets)
    {
        var config = secrets.Read();
        var labels = new Dictionary<string, string>
        {
            ["NVIDIA_API_KEY"] = "NVIDIA API key (server only)",
            ["TFNSW_API_KEY"] = "Transport for NSW Trip Planner API key (server only)",
            ["GOOGLE_SERVER_KEY"] = "Google server key (Places / Routes / Weather)",
            ["GOOGLE_IOS_KEY"] = "Google iOS key (retained iOS client)"
        };
        var fields = string.Join("", labels.Select(pair => $"<label>{pair.Value}<input type=\"password\" name=\"{pair.Key}\" autocomplete=\"off\" placeholder=\"{(config[pair.Key].Length > 0 ? "Configured; leave blank to keep" : "Not configured")}\"></label>"));
        return $$"""
            <!doctype html><html lang="zh"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Sydney Buddy · Local setup</title>
            <style>body{font:16px system-ui;background:#f8f7ef;color:#153e35;max-width:640px;margin:60px auto;padding:24px}label{display:block;margin:24px 0}input{display:block;box-sizing:border-box;width:100%;padding:15px;border-radius:12px;border:1px solid #bcd8cb;margin-top:8px}button{background:#138564;color:white;border:0;border-radius:12px;padding:16px}p{line-height:1.7}</style>
            <h1>🧭 Sydney Buddy</h1><p>Private settings are saved locally. Server keys are not sent to mobile clients. Leave fields blank to keep existing values.</p>
            <form method="post" action="/setup"><input type="hidden" name="token" value="{{WebUtility.HtmlEncode(secrets.SetupToken)}}">{{fields}}<button>Save locally</button></form>
            <p>Google requires billing and the relevant APIs to be enabled. Rebuild the iOS app after changing its map key.</p></html>
            """;
    }
}
