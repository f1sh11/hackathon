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
            await ErrorAsync(context, 403, "Only local requests are supported");
            return;
        }
        try { await next(context); }
        catch (RevisionConflictException exception) { await ErrorAsync(context, 409, exception.Message); }
        catch (ServiceException exception) { await ErrorAsync(context, 422, exception.Message); }
        catch (JsonException) { await ErrorAsync(context, 400, "Invalid request format"); }
        catch (BadHttpRequestException exception) { await ErrorAsync(context, exception.StatusCode, "Invalid request format or size"); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
        catch (Exception exception)
        {
            // 只记录异常类型与编号，避开原始异常正文。 / Omit raw exception messages that may contain private data.
            logger.LogError("Unhandled {ExceptionType}; TraceId {TraceId}", exception.GetType().Name, context.TraceIdentifier);
            await ErrorAsync(context, 500, "The service could not complete the request. Your original trip is unchanged.");
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
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
}
