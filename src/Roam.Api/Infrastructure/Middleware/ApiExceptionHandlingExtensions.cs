using System.Text.Json;
using Npgsql;
using Roam.Api.Infrastructure.Errors;

namespace Roam.Api.Infrastructure.Middleware;

public static class ApiExceptionHandlingExtensions
{
    public static WebApplication UseApiExceptionHandling(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
                context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";

            try
            {
                await next(context);
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                var (code, message) = ex switch
                {
                    ConflictException => (409, ex.Message),
                    MissingException => (404, ex.Message),
                    ArgumentException => (400, ex.Message),
                    BadHttpRequestException => (400, "请求格式不正确，请检查输入。"),
                    JsonException => (400, "请求内容不是有效的 JSON。"),
                    NpgsqlException => (503, "数据库暂时不可用，请稍后重试。"),
                    _ => (500, "生成时出现问题，请重试；你的已保存任务不会被覆盖。")
                };

                if (code >= 500) app.Logger.LogError(ex, "Request failed: {Path}", context.Request.Path);
                context.Response.StatusCode = code;
                await context.Response.WriteAsJsonAsync(new { message });
            }
        });

        return app;
    }
}
