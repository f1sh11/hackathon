namespace SydneyBuddy.Api;

/// <summary>保存单个行程快照及其并发版本；stores one itinerary JSON snapshot and its concurrency revision.</summary>
public sealed class TripEntity
{
    public required string Id { get; set; }
    public required string DataJson { get; set; }
    public int Revision { get; set; }
}
