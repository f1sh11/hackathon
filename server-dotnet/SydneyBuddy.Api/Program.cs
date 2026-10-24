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

app.UseRouting();
app.UseMiddleware<TravelDataMiddleware>();
app.MapBuddyApi("");
app.MapFallback("{**path}", (HttpContext context) => Results.Json(new { error = "未找到" }, statusCode: 404));
app.Run();

/// <summary>供集成测试托管真实 API。 / Entry point exposed to the in-process integration test host.</summary>
public partial class Program;
