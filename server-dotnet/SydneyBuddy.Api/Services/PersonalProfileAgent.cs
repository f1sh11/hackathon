using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

// The personal introduction shares transport and consent, but never the trip questionnaire.
public static class PersonalProfileAgent
{
    public const string Purpose = "personal_profile";
    public static bool Enabled(PetSession session) => session.Purpose == Purpose;
    public static string Prompt { get; } = ReadPrompt();

    private static string ReadPrompt()
    {
        using var stream = typeof(PersonalProfileAgent).Assembly.GetManifestResourceStream("SydneyBuddy.Api.Prompts.PersonalProfile.md")
            ?? throw new InvalidOperationException("The personal profile agent prompt is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static void Enable(PetSession session, JsonObject input)
    {
        session.Purpose = Purpose;
        session.PersonalizingTrip = false;
        session.InteractionContext = "";
        var nickname = input.Str("nickname").Trim();
        if (nickname.Length > 24 || nickname.Any(char.IsControl)) nickname = "";
        session.Dialogue = new() { Version = 3, Phase = "personal_profile", Nickname = nickname };
    }

    public static JsonObject Context(PetSession session, JsonArray memories) => new()
    {
        ["purpose"] = Purpose,
        ["personalProfile"] = session.PersonalProfile.DeepClone(),
        ["confirmedPreferences"] = memories.DeepClone(),
        ["pendingPreferences"] = JsonSerializer.SerializeToNode(session.Preferences.Where(x => x.Status == "awaiting_confirmation"), JsonExtensions.Options),
        ["preferenceKeys"] = JsonSerializer.SerializeToNode(TravelProfiling.Keys.Keys),
        ["preferenceDimensions"] = JsonSerializer.SerializeToNode(TravelProfiling.Keys.ToDictionary(x => x.Key, x => x.Value.Dimension)),
        ["currentChoice"] = JsonSerializer.SerializeToNode(session.CurrentChoice, JsonExtensions.Options),
        ["opening"] = session.Messages.Count == 0,
        ["nickname"] = session.Dialogue?.Nickname ?? "",
        ["initialResponsesRemaining"] = Math.Max(0, KokoDialogue.MaximumResponses - (session.Dialogue?.ResponseCount ?? 0) - (session.Messages.Count == 0 ? 0 : 1))
    };

    public static PetQuickReply[] Replies(JsonObject result)
    {
        if (string.IsNullOrWhiteSpace(result.Str("reply")) || result.Str("reply").Length > 2000 || result.Array("quickReplies") is not { Count: 3 } raw)
            throw InvalidResponse();
        var replies = new List<PetQuickReply>();
        foreach (var node in raw)
        {
            if (node is not JsonObject option) throw InvalidResponse();
            var text = option.Str("text").Trim();
            var intent = option.Str("intent");
            if (text.Length is 0 or > 45 || text.Any(char.IsControl) || intent is not ("respond" or "skip" or "finish_profile")) throw InvalidResponse();
            replies.Add(new(Guid.NewGuid().ToString("N"), text, intent));
        }
        if (replies.Select(x => x.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3) throw InvalidResponse();
        return replies.ToArray();
    }

    public static PetMessage Opening(PetSession session, JsonObject result) =>
        new(Guid.NewGuid().ToString("N"), "assistant", result.Str("reply"), PetEmotion.Normalize(result.Str("mood")), Replies(result));

    public static PetMessage CompleteTurn(PetSession session, JsonObject result)
    {
        var replies = Replies(result);
        var dialogue = session.Dialogue!;
        dialogue.ResponseCount++;
        if (result.Bool("readyForDraft") || dialogue.ResponseCount >= KokoDialogue.MaximumResponses) dialogue.Phase = "profile_ready";
        return new(Guid.NewGuid().ToString("N"), "assistant", result.Str("reply"), PetEmotion.Normalize(result.Str("mood")), replies);
    }

    private static ServiceException InvalidResponse() => new("Koko couldn't prepare three choices. Your previous answers are safe; please try again.");
}
