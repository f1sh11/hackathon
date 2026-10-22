namespace Roam.Core;

public sealed record TravelerProfile(Guid Id, int[] Answers, int[] Values, string Character, DateTimeOffset CreatedAt);

public sealed record PlanningRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, decimal Budget,
    int Energy, string Theme, string Notes, bool Easier = false);

public sealed record ItineraryStop(string Id, string Name, string Category, string StartTime, string EndTime,
    int TransferMinutes, int DurationMinutes, decimal Cost, decimal WalkKm, int RemainingHp, string Description);

public sealed record TripPlan(ItineraryStop[] Stops, int RemainingHp, decimal TotalCost, decimal WalkKm,
    string ReturnTime, string[] Reasons, string DataNotice);

public sealed record PlanningResult(bool Feasible, string[] Conflicts, TripPlan? Plan);

public static class ProfileFactory
{
    public static TravelerProfile Create(int[] answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.Length != 6 || answers.Any(answer => answer is < 0 or > 2))
            throw new ArgumentException("请完成六个问题，每题选择一个有效选项。", nameof(answers));
        var copy = answers.ToArray();
        var values = copy.Select(answer => new[] { 25, 55, 85 }[answer]).ToArray();
        var character = values[2] < 40 ? "留白旅行家" : values[4] > 70 ? "城市探索家" : "自在漫游者";
        return new TravelerProfile(Guid.NewGuid(), copy, values, character, DateTimeOffset.UtcNow);
    }
}

/// <summary>Every value is illustrative fixture data, including the local grid, opening hours and price.</summary>
public sealed record SamplePlace(string Id, string Name, string Category, string[] Themes,
    int DurationMinutes, decimal Cost, decimal OnsiteWalkKm, double X, double Y, int Slope,
    int Exploration, int Uncertainty, int? OpensAt, int? ClosesAt, bool IsBreak, bool IsMeal,
    int RecoveryHp, string Description);
