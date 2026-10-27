using System.Text.Json;
using System.Diagnostics;

namespace SydneyBuddy.Api;

/// <summary>
/// 限制本机访问、统一安全响应头和错误 JSON；不把内部异常或密钥发给用户。
/// Enforces local access and consistent headers/errors without exposing internal failures or secrets.
/// </summary>
public sealed class LocalRequestMiddleware(RequestDelegate next, ILogger<LocalRequestMiddleware> logger)
{
    private static readonly HashSet<string> AllowedOrigins = [
        "http://127.0.0.1:8787", "http://localhost:8787"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        var origin = context.Request.Headers.Origin.ToString();
        if (context.Request.Host.Host is not ("127.0.0.1" or "localhost") ||
            (origin.Length > 0 && !AllowedOrigins.Contains(origin)))
        {
            await ErrorAsync(context, 403, "仅支持本机请求");
            return;
        }
        try { using var skill=context.Request.Path.StartsWithSegments("/admin/planning/skills") ? ItinerarySkill.Use(ItinerarySkill.Load(null)) : ItinerarySkill.BeginRequest(context.RequestServices.GetRequiredService<SkillAdministration>().ActivePath); await next(context); }
        catch (RevisionConflictException exception) { await ErrorAsync(context, 409, exception.Message); }
        catch (ServiceException exception) { await ErrorAsync(context, 422, exception.Message); }
        catch (JsonException) { await ErrorAsync(context, 400, "请求格式不合法"); }
        catch (BadHttpRequestException exception) { await ErrorAsync(context, exception.StatusCode, "请求格式或大小不合法"); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
        catch (Exception exception)
        {
            // 只记录异常类型与编号，避开原始异常正文。 / Omit raw exception messages that may contain private data.
            logger.LogError("Unhandled {ExceptionType}; TraceId {TraceId}", exception.GetType().Name, context.TraceIdentifier);
            await ErrorAsync(context, 500, "服务处理失败，原行程保持不变");
        }
        finally
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
            logger.LogInformation("HTTP {Method} {Route} returned {StatusCode} in {ElapsedMs} ms; TraceId {TraceId}",
                context.Request.Method, route, response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
        }
    }

    private static Task ErrorAsync(HttpContext context, int statusCode, string message)
    {
        if(context.Request.Path.StartsWithSegments("/agents/feasibility"))message=statusCode switch {
            400=>"The review request is incomplete or invalid.",
            403=>"This endpoint accepts local requests only.",
            409=>"This trip or its requirements changed. Check the latest version before saving.",
            413=>"This draft is too large. Send its trip ID or remove adjustment history.",
            500=>"The review couldn't be completed. Your saved trip is unchanged.",
            _ when message.Any(c=>c is >= '\u3400' and <= '\u9fff')=>"This draft couldn't be checked or confirmed. Review its details and try again.",
            _=>message
        };
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error = message, runId = context.Items[typeof(PlanningRunJournal)] as string });
    }
}
