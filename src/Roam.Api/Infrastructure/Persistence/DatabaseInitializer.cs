using Npgsql;

namespace Roam.Api.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var store = app.Services.GetRequiredService<TripStore>();
        var schema = await File.ReadAllTextAsync(Path.Combine(app.Environment.ContentRootPath, "Database", "001_initial.sql"));

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await store.InitializeAsync(schema, CancellationToken.None);
                break;
            }
            catch (NpgsqlException) when (attempt < 19)
            {
                app.Logger.LogWarning("Waiting for PostgreSQL ({Attempt}/20)", attempt + 1);
                await Task.Delay(500);
            }
        }
    }
}
