using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SydneyBuddy.Api;

namespace KokoApiFixture;

/// <summary>Test-only bootstrap. Routes, validation, persistence, NVIDIA parsing and H-MEM belong to the application.</summary>
internal static class FixtureProgram
{
    public static async Task<int> Main(string[] args)
    {
        OwnedEnvironment? owned=null;
        try
        {
            var connection=Environment.GetEnvironmentVariable("KOKO_TEST_CONNECTION");
            if(string.IsNullOrWhiteSpace(connection))throw new InvalidOperationException("KOKO_TEST_CONNECTION is required.");
            var mode=Environment.GetEnvironmentVariable("KOKO_TEST_MODE") ?? "fixture";
            if(mode is not ("fixture" or "live"))throw new InvalidOperationException("KOKO_TEST_MODE must be fixture or live.");
            if(mode=="live" && Environment.GetEnvironmentVariable("KOKO_TEST_LIVE_OPT_IN")!="1")
                throw new InvalidOperationException("Live mode requires KOKO_TEST_LIVE_OPT_IN=1.");
            var port=int.TryParse(Environment.GetEnvironmentVariable("KOKO_TEST_PORT"),out var suppliedPort) ? suppliedPort:8897;
            if(port is <1024 or >65535)throw new InvalidOperationException("Use an unprivileged test port.");

            // These are child-process settings only. No production/team secret file is read.
            Environment.SetEnvironmentVariable("NVIDIA_API_KEY",mode=="fixture" ? "koko_fixture_transport_only":Environment.GetEnvironmentVariable("KOKO_TEST_NVIDIA_API_KEY") ?? "");
            Environment.SetEnvironmentVariable("NVIDIA_MODEL",Environment.GetEnvironmentVariable("KOKO_TEST_MODEL") ?? (mode=="fixture" ? "koko/scripted-sse-fixture":SecretStore.DefaultModel));
            foreach(var name in new[]{"GOOGLE_SERVER_KEY","GOOGLE_IOS_KEY","TFNSW_API_KEY"})Environment.SetEnvironmentVariable(name,"");

            var repo=Environment.GetEnvironmentVariable("SYDNEYBUDDY_ROOT") ?? ProjectPaths.DiscoverRoot();
            owned=await OwnedEnvironment.CreateAsync(connection,Environment.GetEnvironmentVariable("KOKO_TEST_DATA"));
            var paths=new ProjectPaths(repo,owned.DataDirectory);
            var state=new FixtureState(mode);
            var builder=WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=250_000);
            builder.Logging.AddFilter("System.Net.Http.HttpClient",LogLevel.None);
            builder.Services.ConfigureHttpJsonOptions(options=>options.SerializerOptions.Encoder=JsonExtensions.Options.Encoder);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["ConnectionStrings:SydneyBuddy"]=owned.ConnectionString,
                ["AllowPaidServices"]="false",
                ["Navigation:ValhallaUrl"]="",
                ["Transit:OtpGraphqlUrl"]="",
                ["SYDNEYBUDDY_ROOT"]=repo,
                ["SYDNEYBUDDY_DATA"]=owned.DataDirectory,
                ["TravelData:ManagedSource"]="direct"
            });
            builder.Services.AddBuddyServices();
            builder.Services.RemoveAll<ProjectPaths>();builder.Services.AddSingleton(paths);
            builder.Services.RemoveAll<IPetModel>();
            builder.Services.AddSingleton<IPetModel>(provider=>new PetModel(new HttpClient(
                mode=="fixture" ? new ScriptedNvidiaHandler(state):new ObservedLiveHandler(state))
                {Timeout=Timeout.InfiniteTimeSpan},provider.GetRequiredService<SecretStore>()));
            var app=builder.Build();
            app.UseMiddleware<LocalRequestMiddleware>();
            app.Use(async(context,next)=>
            {
                var path=context.Request.Path;
                var allowed=path=="/health" || path.StartsWithSegments("/pet") ||
                    path.StartsWithSegments("/memories") || path.StartsWithSegments("/__koko_test__");
                if(!allowed){context.Response.StatusCode=404;await context.Response.WriteAsJsonAsync(new{error="This test host exposes only dialog, memory and test metadata routes."});return;}
                await next(context);
            });
            await using(var scope=app.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<BuddyDbContext>().Database.MigrateAsync();
            app.UseRouting();app.UseMiddleware<TravelDataMiddleware>();app.MapBuddyApi("");
            app.MapGet("/__koko_test__/metadata",(SecretStore secrets)=>
            {
                var settings=secrets.Read();
                var now=DateTimeOffset.UtcNow;
                return Results.Json(new
                {
                    isolated=true,environmentId=owned.EnvironmentId,mode,
                    modelConfigured=mode=="fixture" || settings["NVIDIA_API_KEY"].Length>0,
                    model=settings["NVIDIA_MODEL"],schemaPrefix="koko_cli_",fixtureApiVersion=1,
                    modelCalls=state.TotalCalls,
                    clock=new{source="system",utc=now.ToString("O"),sydney=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"Australia/Sydney").ToString("O"),timeZone="Australia/Sydney"},
                    promptHashes=new{travelProfiling=Digest(TravelProfiling.SystemPrompt),dialogue=Digest(KokoDialogue.Prompt),companion=Digest(KokoCompanion.Prompt)}
                });
            });
            app.MapGet("/__koko_test__/fixture",()=>Results.Json(state.Snapshot()));
            app.MapPost("/__koko_test__/fixture",async(HttpContext context)=>
            {
                if(mode!="fixture")return Results.Json(new{error="Scripted responses are disabled in live mode."},statusCode:409);
                var input=await context.Request.ReadFromJsonAsync<JsonObject>(context.RequestAborted) ?? throw new ServiceException("A fixture object is required.");
                state.Configure(input);
                return Results.Json(state.Snapshot());
            });
            Console.WriteLine($"Koko test host ready: environmentId={owned.EnvironmentId}; mode={mode}; http://127.0.0.1:{port}");
            await app.RunAsync();
            return 0;
        }
        catch(Exception error)
        {
            // A connection string or transport exception can contain secrets. Log the type only.
            Console.Error.WriteLine($"Koko test host failed ({error.GetType().Name}). Check KOKO_TEST_CONNECTION, mode/opt-in, and owned test directory.");
            return 2;
        }
        finally
        {
            if(owned is not null && Environment.GetEnvironmentVariable("KOKO_TEST_CLEANUP_ON_EXIT")=="1")await owned.CleanupAsync();
        }
    }

    internal static string Digest(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>A marked directory and random PostgreSQL schema form a restartable, isolated test environment.</summary>
internal sealed class OwnedEnvironment
{
    public required string EnvironmentId{get;init;}
    public required string Schema{get;init;}
    public required string DataDirectory{get;init;}
    public required string BaseConnection{get;init;}
    public required string ConnectionString{get;init;}
    private string Manifest=>Path.Combine(DataDirectory,"koko-test-environment.json");
    public static async Task<OwnedEnvironment> CreateAsync(string connection,string? directory)
    {
        var settings=new NpgsqlConnectionStringBuilder(connection);
        var fingerprint=FixtureProgram.Digest($"{settings.Host}|{settings.Port}|{settings.Database}|{settings.Username}");
        var id=Guid.NewGuid().ToString("N");
        var root=Path.GetFullPath(directory ?? Path.Combine(Path.GetTempPath(),"koko-api-fixture-"+id));
        var manifest=Path.Combine(root,"koko-test-environment.json");
        var resuming=File.Exists(manifest);
        if(resuming)
        {
            var existing=JsonNode.Parse(await File.ReadAllTextAsync(manifest))!.AsObject();
            id=existing.Str("environmentId");
            if(existing.Str("owner")!="koko-api-test-harness" || existing.Str("connectionFingerprint")!=fingerprint || !Guid.TryParseExact(id,"N",out _))
                throw new InvalidOperationException("The directory is not owned by this test environment.");
        }
        else if(Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("Refusing to use a nonempty unmarked directory.");
        var schema="koko_cli_"+id;
        if(!Regex.IsMatch(schema,"^koko_cli_[a-f0-9]{32}$"))throw new InvalidOperationException("Invalid owned schema.");
        Directory.CreateDirectory(root);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        await using(var database=new NpgsqlConnection(connection))
        {
            await database.OpenAsync();
            await using var command=new NpgsqlCommand($"CREATE SCHEMA {(resuming ? "IF NOT EXISTS ":"")}{schema}",database);
            await command.ExecuteNonQueryAsync();
        }
        await File.WriteAllTextAsync(manifest,JsonSerializer.Serialize(new{owner="koko-api-test-harness",environmentId=id,connectionFingerprint=fingerprint,schema}));
        settings.SearchPath=schema;settings.MaxPoolSize=5;
        return new(){EnvironmentId=id,Schema=schema,DataDirectory=root,BaseConnection=connection,ConnectionString=settings.ConnectionString};
    }
    public async Task CleanupAsync()
    {
        try
        {
            var marker=JsonNode.Parse(await File.ReadAllTextAsync(Manifest))!.AsObject();
            if(marker.Str("owner")!="koko-api-test-harness" || marker.Str("environmentId")!=EnvironmentId || marker.Str("schema")!=Schema)return;
            await using var database=new NpgsqlConnection(BaseConnection);await database.OpenAsync();
            await using var command=new NpgsqlCommand($"DROP SCHEMA IF EXISTS {Schema} CASCADE",database);await command.ExecuteNonQueryAsync();
            Directory.Delete(DataDirectory,recursive:true);
        }
        catch(Exception error){Console.Error.WriteLine($"Owned test cleanup incomplete ({error.GetType().Name}); existing user data was not targeted.");}
    }
}
