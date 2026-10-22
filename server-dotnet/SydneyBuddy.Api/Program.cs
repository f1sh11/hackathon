using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using SydneyBuddy.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8787");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 250_000);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Encoder = JsonExtensions.Options.Encoder);
builder.Services.AddBuddyServices();

var app = builder.Build();
app.UseMiddleware<LocalRequestMiddleware>();
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<BuddyDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("PostgreSQL migrations applied. Local API ready on port 8787.");
}

// 网页构建后由同一服务托管；Android/iOS 继续使用原接口。
// Serve the built React app alongside the unchanged Android/iOS endpoints.
var paths = app.Services.GetRequiredService<ProjectPaths>();
if (Directory.Exists(paths.WebDist))
{
    var files = new PhysicalFileProvider(paths.WebDist);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}
// 先提供网页文件，再匹配 API/404；否则 fallback 会阻止静态文件中间件。
// Resolve static files before endpoint routing so the 404 fallback cannot intercept them.
app.UseRouting();
app.MapBuddyApi("");
app.MapBuddyApi("/api");
app.MapFallback((HttpContext context) => Results.Json(new { error = "未找到" }, statusCode: 404));
app.Run();

/// <summary>供集成测试托管真实 API。 / Entry point exposed to the in-process integration test host.</summary>
public partial class Program;
