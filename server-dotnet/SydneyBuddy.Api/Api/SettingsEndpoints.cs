using System.Security.Cryptography;
using System.Text;

namespace SydneyBuddy.Api;

/// <summary>本机配置表单与 CSRF 校验；protects local secret updates with a form token.</summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/setup", (SecretStore secrets) => Results.Content(SetupPage.Render(secrets), "text/html; charset=utf-8"));
        routes.MapPost("/setup", SaveAsync);
    }

    private static async Task<IResult> SaveAsync(HttpContext context, SecretStore secrets)
    {
        if (context.Request.ContentLength > 10000) return Results.Json(new { error = "请求过大" }, statusCode: 400);
        if (!context.Request.HasFormContentType) return Results.Json(new { error = "请求格式不合法" }, statusCode: 400);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var token = form["token"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(secrets.SetupToken)))
            return Results.Json(new { error = "设置页面已过期，请刷新" }, statusCode: 403);
        secrets.Save(form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()));
        return Results.Content("<meta charset=\"utf-8\"><h1>已保存到本机 / Saved locally</h1><p>关闭本页即可。iOS 地图密钥变更后需重新构建。</p>", "text/html; charset=utf-8");
    }
}
