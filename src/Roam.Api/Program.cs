using Roam.Api.Features.Profiles;
using Roam.Api.Features.System;
using Roam.Api.Features.Trips;
using Roam.Api.Infrastructure;
using Roam.Api.Infrastructure.Middleware;
using Roam.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddRoamServices(builder.Configuration);

var app = builder.Build();
app.UseApiExceptionHandling();
await app.InitializeDatabaseAsync();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapSystemEndpoints();
app.MapProfileEndpoints();
app.MapTripEndpoints();
app.Map("/api/{**path}", () => Results.NotFound(new { message = "没有这个接口。" }));
app.MapFallbackToFile("index.html");
app.Run();
