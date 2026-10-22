using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using Roam.Api.Infrastructure.Persistence;
using Roam.Core;

namespace Roam.Api.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddRoamServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 64 * 1024);
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        var connectionString = configuration.GetConnectionString("Roam")
            ?? Environment.GetEnvironmentVariable("ROAM_DATABASE_URL")
            ?? throw new InvalidOperationException("Configure ConnectionStrings__Roam with a PostgreSQL connection string. No SQLite or in-memory fallback is used.");

        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<TripStore>();
        services.AddSingleton<Planner>();
        return services;
    }
}
