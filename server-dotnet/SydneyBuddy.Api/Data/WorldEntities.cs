namespace SydneyBuddy.Api;

/// <summary>One stable, trip-scoped World Model root. Its state is reconstructed from immutable runs, snapshots and events.</summary>
public sealed class TravelWorldEntity
{
    public required string Id { get; set; }
    public required string TripId { get; set; }
    public int SchemaVersion { get; set; }
    public required string CreatedAt { get; set; }
}

/// <summary>A bounded simulation or optimization attempt. A client request id makes retries idempotent.</summary>
public sealed class WorldRunEntity
{
    public required string Id { get; set; }
    public required string WorldId { get; set; }
    public required string RequestId { get; set; }
    public required string Kind { get; set; }
    public int TripRevision { get; set; }
    public required string Status { get; set; }
    public required string CreatedAt { get; set; }
    public string? CompletedAt { get; set; }
}

/// <summary>An append-only, complete state observation emitted while a World Model run advances.</summary>
public sealed class WorldSnapshotEntity
{
    public required string Id { get; set; }
    public required string WorldId { get; set; }
    public required string RunId { get; set; }
    public int Sequence { get; set; }
    public required string OccurredAt { get; set; }
    public required string DataJson { get; set; }
}

/// <summary>An append-only fact about a run, including a user's accept or reject decision.</summary>
public sealed class WorldEventEntity
{
    public required string Id { get; set; }
    public required string WorldId { get; set; }
    public required string RunId { get; set; }
    public required string Type { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string Actor { get; set; }
    public required string OccurredAt { get; set; }
    public required string DataJson { get; set; }
}
