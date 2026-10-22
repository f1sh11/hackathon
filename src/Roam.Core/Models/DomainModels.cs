namespace Roam.Core;

public sealed record TravelerProfile(Guid Id, int[] Answers, int[] Values, string Character, DateTimeOffset CreatedAt);

public sealed record PlanningRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, decimal Budget,
    int Energy, string Theme, string Notes, bool Easier = false);

public sealed record ItineraryStop(string Id, string Name, string Category, string StartTime, string EndTime,
    int TransferMinutes, int DurationMinutes, decimal Cost, decimal WalkKm, int RemainingHp, string Description);

public sealed record TripPlan(ItineraryStop[] Stops, int RemainingHp, decimal TotalCost, decimal WalkKm,
    string ReturnTime, string[] Reasons, string DataNotice);

public sealed record PlanningResult(bool Feasible, string[] Conflicts, TripPlan? Plan);

/// <summary>Every value is illustrative fixture data, including the local grid, opening hours and price.</summary>
public sealed record SamplePlace(string Id, string Name, string Category, string[] Themes,
    int DurationMinutes, decimal Cost, decimal OnsiteWalkKm, double X, double Y, int Slope,
    int Exploration, int Uncertainty, int? OpensAt, int? ClosesAt, bool IsBreak, bool IsMeal,
    int RecoveryHp, string Description);
