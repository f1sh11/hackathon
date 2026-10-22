using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Roam.Api.Contracts;
using Roam.Api.Infrastructure.Errors;
using Roam.Core;

namespace Roam.Api.Infrastructure.Persistence;

public sealed class TripStore(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static NpgsqlParameter JsonParam(string name, object value) => new(name, NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(value, Json) };
    public async Task InitializeAsync(string schema, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(8301022);" + schema, connection, tx);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT 1");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }
    public async Task<TravelerProfile?> GetProfileAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT id,answers,values,character,created_at FROM profiles LIMIT 1");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetGuid(0), JsonSerializer.Deserialize<int[]>(reader.GetString(1), Json)!,
            JsonSerializer.Deserialize<int[]>(reader.GetString(2), Json)!, reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4));
    }
    public async Task<TravelerProfile> CreateProfileAsync(int[] answers, CancellationToken ct)
    {
        var profile = ProfileFactory.Create(answers);
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO profiles(id,answers,values,character,created_at) VALUES (@id,@answers,@values,@character,@created)
            ON CONFLICT (singleton) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("id", profile.Id);
        cmd.Parameters.Add(JsonParam("answers", profile.Answers));
        cmd.Parameters.Add(JsonParam("values", profile.Values));
        cmd.Parameters.AddWithValue("character", profile.Character);
        cmd.Parameters.AddWithValue("created", profile.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
        var saved = (await GetProfileAsync(ct))!;
        if (!saved.Answers.SequenceEqual(answers)) throw new ConflictException("旅行画像已经固定，不能通过修改分数或重新提交覆盖。请在任务中调整本次要求。");
        return saved;
    }
    public async Task<TripSummary[]> ListTripsAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT t.id,t.title,t.status,t.created_at,coalesce(max(v.version),0),t.request
            FROM trips t LEFT JOIN trip_versions v ON v.trip_id=t.id
            GROUP BY t.id ORDER BY t.created_at DESC LIMIT 100
            """);
        var rows = new List<TripSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0), reader.GetString(1),reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),reader.GetInt32(4),JsonSerializer.Deserialize<TripRequestSnapshot>(reader.GetString(5),Json)!));
        return rows.ToArray();
    }
    public async Task<TripDetail> GetTripAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        await using var cmd = new NpgsqlCommand("SELECT title,status,created_at,request FROM trips WHERE id=@id",connection,tx);
        cmd.Parameters.AddWithValue("id", id);
        string title,status; DateTimeOffset created; TripRequestSnapshot request;
        await using(var reader=await cmd.ExecuteReaderAsync(ct))
        {
            if(!await reader.ReadAsync(ct)) throw new MissingException("没有找到这个旅行任务。");
            title=reader.GetString(0);status=reader.GetString(1);created=reader.GetFieldValue<DateTimeOffset>(2);
            request=JsonSerializer.Deserialize<TripRequestSnapshot>(reader.GetString(3),Json)!;
        }
        await using var versionsCmd = new NpgsqlCommand("SELECT version,created_at,easier,plan,conflicts FROM trip_versions WHERE trip_id=@id ORDER BY version",connection,tx);
        versionsCmd.Parameters.AddWithValue("id",id);
        var versions=new List<TripVersion>();
        await using(var reader=await versionsCmd.ExecuteReaderAsync(ct))
        {
            while(await reader.ReadAsync(ct)) versions.Add(new(reader.GetInt32(0),reader.GetFieldValue<DateTimeOffset>(1),reader.GetBoolean(2),
                reader.IsDBNull(3)?null:JsonSerializer.Deserialize<TripPlan>(reader.GetString(3),Json),
                JsonSerializer.Deserialize<string[]>(reader.GetString(4),Json)!));
        }
        await tx.CommitAsync(ct);
        return new(id,title,status,created,versions.Count==0?0:versions[^1].Version,request,versions.ToArray());
    }
    public async Task<TripDetail> CreateTripAsync(TripRequestSnapshot request, Guid key, PlanningResult result, CancellationToken ct)
    {
        var id=Guid.NewGuid(); var created=DateTimeOffset.UtcNow;
        await using var connection=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        // Lock by idempotency key so retries and simultaneous clicks cannot create duplicate tasks.
        await using(var lockCmd=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",connection,tx))
        {lockCmd.Parameters.AddWithValue("key",key.ToString());await lockCmd.ExecuteNonQueryAsync(ct);}
        await using(var existing=new NpgsqlCommand("SELECT id,request FROM trips WHERE idempotency_key=@key",connection,tx))
        {
            existing.Parameters.AddWithValue("key",key);
            await using var reader=await existing.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))
            {
                var existingId=reader.GetGuid(0);
                if(JsonSerializer.Deserialize<TripRequestSnapshot>(reader.GetString(1),Json)!=request)
                    throw new ConflictException("相同请求编号对应不同内容，请重新创建任务。");
                await reader.DisposeAsync();await tx.CommitAsync(ct);
                return await GetTripAsync(existingId,ct);
            }
        }
        await using(var usedKey=new NpgsqlCommand("SELECT 1 FROM trip_versions WHERE idempotency_key=@key",connection,tx))
        {
            usedKey.Parameters.AddWithValue("key",key);
            if(await usedKey.ExecuteScalarAsync(ct) is not null)
                throw new ConflictException("这个请求编号已经用于重新规划，请使用新的请求编号。");
        }
        await using(var insert=new NpgsqlCommand("INSERT INTO trips(id,profile_id,title,request,status,idempotency_key,created_at) VALUES (@id,@profile,@title,@request,@status,@key,@created)",connection,tx))
        {
            insert.Parameters.AddWithValue("id",id);insert.Parameters.AddWithValue("profile",request.ProfileId);
            insert.Parameters.AddWithValue("title",request.Title);insert.Parameters.Add(JsonParam("request",request));
            insert.Parameters.AddWithValue("status",result.Feasible?"completed":"infeasible");insert.Parameters.AddWithValue("key",key);
            insert.Parameters.AddWithValue("created",created);await insert.ExecuteNonQueryAsync(ct);
        }
        await InsertVersionAsync(connection,tx,id,1,false,key,result,ct);
        await tx.CommitAsync(ct);
        return await GetTripAsync(id,ct);
    }
    public async Task<TripDetail> ReplanAsync(Guid id, Guid key, bool easier, PlanningResult result, CancellationToken ct)
    {
        await using var connection=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        // Consistent lock order serializes append operations for a trip and re-used request keys.
        await using(var lockCmd=new NpgsqlCommand("SELECT id FROM trips WHERE id=@id FOR UPDATE",connection,tx))
        {lockCmd.Parameters.AddWithValue("id",id);if(await lockCmd.ExecuteScalarAsync(ct) is null)throw new MissingException("没有找到这个旅行任务。");}
        await using(var keyLock=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",connection,tx))
        {keyLock.Parameters.AddWithValue("key",key.ToString());await keyLock.ExecuteNonQueryAsync(ct);}
        await using(var existing=new NpgsqlCommand("SELECT trip_id,easier FROM trip_versions WHERE idempotency_key=@key",connection,tx))
        {
            existing.Parameters.AddWithValue("key",key);await using var reader=await existing.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))
            {
                if(reader.GetGuid(0)!=id||reader.GetBoolean(1)!=easier)throw new ConflictException("请求编号已用于其他操作，请重试。");
                await reader.DisposeAsync();await tx.CommitAsync(ct);return await GetTripAsync(id,ct);
            }
        }
        int version;
        await using(var getVersion=new NpgsqlCommand("SELECT coalesce(max(version),0)+1 FROM trip_versions WHERE trip_id=@id",connection,tx))
        {getVersion.Parameters.AddWithValue("id",id);version=Convert.ToInt32(await getVersion.ExecuteScalarAsync(ct));}
        await InsertVersionAsync(connection,tx,id,version,easier,key,result,ct);
        await using(var update=new NpgsqlCommand("UPDATE trips SET status=@status WHERE id=@id",connection,tx))
        {update.Parameters.AddWithValue("id",id);update.Parameters.AddWithValue("status",result.Feasible?"completed":"infeasible");await update.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);return await GetTripAsync(id,ct);
    }
    private static async Task InsertVersionAsync(NpgsqlConnection connection,NpgsqlTransaction tx,Guid id,int version,bool easier,Guid key,PlanningResult result,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("INSERT INTO trip_versions(trip_id,version,easier,plan,conflicts,idempotency_key,created_at) VALUES (@id,@version,@easier,@plan,@conflicts,@key,@created)",connection,tx);
        cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("version",version);cmd.Parameters.AddWithValue("easier",easier);
        cmd.Parameters.Add(result.Plan is null?new NpgsqlParameter("plan",NpgsqlDbType.Jsonb){Value=DBNull.Value}:JsonParam("plan",result.Plan));
        cmd.Parameters.Add(JsonParam("conflicts",result.Conflicts));cmd.Parameters.AddWithValue("key",key);cmd.Parameters.AddWithValue("created",DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
