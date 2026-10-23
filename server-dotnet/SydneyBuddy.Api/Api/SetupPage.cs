using System.Net;

namespace SydneyBuddy.Api;

/// <summary>渲染仅包含配置状态的本机设置页。 / Renders local configuration status, never secret values.</summary>
public static class SetupPage
{
    public static string Render(SecretStore secrets)
    {
        var config = secrets.Read();
        var labels = new Dictionary<string, string>
        {
            ["NVIDIA_API_KEY"] = "NVIDIA API key（仅服务端 / server only）",
            ["TFNSW_API_KEY"] = "Transport for NSW Trip Planner API key（仅服务端保存）",
            ["GOOGLE_SERVER_KEY"] = "Google 服务端 key（Places / Routes / Weather）",
            ["GOOGLE_IOS_KEY"] = "Google iOS key（保留的 iOS 客户端 / retained iOS client）"
        };
        var fields = string.Join("", labels.Select(pair => $"<label>{pair.Value}<input type=\"password\" name=\"{pair.Key}\" autocomplete=\"off\" placeholder=\"{(config[pair.Key].Length > 0 ? "已设置，留空保留" : "尚未设置")}\"></label>"));
        return $$"""
            <!doctype html><html lang="zh"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Sydney Buddy · 本机设置</title>
            <style>body{font:16px system-ui;background:#f8f7ef;color:#153e35;max-width:640px;margin:60px auto;padding:24px}label{display:block;margin:24px 0}input{display:block;box-sizing:border-box;width:100%;padding:15px;border-radius:12px;border:1px solid #bcd8cb;margin-top:8px}button{background:#138564;color:white;border:0;border-radius:12px;padding:16px}p{line-height:1.7}</style>
            <h1>🧭 Sydney Buddy</h1><p>私有配置保存在本机，服务端密钥不发送给手机客户端。留空保留已有值。</p>
            <form method="post" action="/setup"><input type="hidden" name="token" value="{{WebUtility.HtmlEncode(secrets.SetupToken)}}">{{fields}}<button>保存到本机 / Save locally</button></form>
            <p>Google 需要启用计费和对应 API。iOS 地图密钥变更后需重新构建。</p></html>
            """;
    }
}
