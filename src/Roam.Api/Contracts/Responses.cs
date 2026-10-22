using Roam.Core;

namespace Roam.Api.Contracts;

public record TripRequestSnapshot(Guid ProfileId, string Title, string Date, string StartTime,
    string EndTime, decimal Budget, int Energy, string Theme, string Notes);

public record TripSummary(Guid Id, string Title, string Status, DateTimeOffset CreatedAt,
    int LatestVersion, TripRequestSnapshot Request);

public record TripVersion(int Version, DateTimeOffset CreatedAt, bool Easier, TripPlan? Plan, string[] Conflicts);

public record TripDetail(Guid Id, string Title, string Status, DateTimeOffset CreatedAt,
    int LatestVersion, TripRequestSnapshot Request, TripVersion[] Versions);
