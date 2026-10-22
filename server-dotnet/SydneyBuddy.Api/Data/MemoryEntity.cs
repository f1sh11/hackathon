namespace SydneyBuddy.Api;

/// <summary>保存用户已授权的偏好展示记录；stores consented preference metadata for API retrieval.</summary>
public sealed class MemoryEntity
{
    public required string Id { get; set; }
    public required string DataJson { get; set; }
}
