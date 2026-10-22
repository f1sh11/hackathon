namespace Roam.Api.Contracts;

public record CreateProfileRequest(int[]? Answers);

public record CreateTripRequest(Guid ProfileId, string? Title, string? Date, string? StartTime,
    string? EndTime, decimal Budget, int Energy, string? Theme, string? Notes, Guid IdempotencyKey);

public record ReplanRequest(bool Easier, Guid IdempotencyKey);
