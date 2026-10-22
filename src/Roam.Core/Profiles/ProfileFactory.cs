namespace Roam.Core;

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
