using System.Reflection;

namespace SydneyBuddy.Api;

public static class ItinerarySkill
{
    public const string Version = "2026-10-27.3";
    public static string Read(string language)
    {
        var name = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-Hant";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SydneyBuddy.Api.Planning.Skills.itinerary." + name + ".md")
            ?? throw new ServiceException("行程規劃 Skill 尚未載入。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
