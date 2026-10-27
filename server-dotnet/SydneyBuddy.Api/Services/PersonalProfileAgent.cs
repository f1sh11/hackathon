using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
        ["personalProfile"] = EffectiveProfile(session,memories),
        ["confirmedPreferences"] = new JsonArray(memories.Where(x=>x?.Object("profile")?.Object("habits") is null).Select(x=>x?.DeepClone()).ToArray()),
        ["recentQuestions"] = JsonSerializer.SerializeToNode(session.Messages.Where(x=>x.Role=="assistant").TakeLast(6).Select(x=>x.Text)),
        ["pendingPreferences"] = JsonSerializer.SerializeToNode(session.Preferences.Where(x => x.Status == "awaiting_confirmation"), JsonExtensions.Options),
        ["preferenceKeys"] = JsonSerializer.SerializeToNode(TravelProfiling.Keys.Keys),
        ["preferenceDefinitions"] = JsonSerializer.SerializeToNode(TravelProfiling.Keys.ToDictionary(x => x.Key, x => new { dimension = x.Value.Dimension, meaning = x.Value.Label })),
        ["currentChoice"] = JsonSerializer.SerializeToNode(session.CurrentChoice, JsonExtensions.Options),
        ["opening"] = session.Messages.Count == 0,
        ["nickname"] = session.Dialogue?.Nickname ?? "",
        ["initialResponsesRemaining"] = Math.Max(0, KokoDialogue.MaximumResponses - (session.Dialogue?.ResponseCount ?? 0) - (session.Messages.Count == 0 ? 0 : 1))
    };

    public static JsonObject EffectiveProfile(PetSession session,JsonArray memories)
    {
        var profile=(JsonObject)session.PersonalProfile.DeepClone();
        foreach(var memory in memories.OfType<JsonObject>().OrderBy(x=>x.Object("provenance")?.Str("createdAt") ?? "")) {
            var fact=TravelProfiling.Read(memory.Object("profile")?.Object("travelDna"));
            if(fact is null || fact.Condition.Length>0 || fact.Status!="confirmed" || fact.Scope!="long_term")continue;
            switch(fact.Key) {
                case "daily_steps":if(int.TryParse(fact.Value.Replace(",",""),out var steps) && steps is >=500 and <=60000)profile["dailySteps"]=steps;break;
                case "queue_tolerance_minutes":profile["queueTolerance"]=int.TryParse(fact.Value,out var minutes) ? minutes==0 ? "Avoid queues":$"Comfortable waiting up to {minutes} minutes":fact.Value;break;
                case "rhythm":profile["rhythm"]=fact.Value;break;
                case "interests":profile["interests"]=JsonSerializer.SerializeToNode(fact.Value.Split([',','、','，',';'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries));break;
            }
        }
        return profile;
    }

    public static void AvoidRepeatedQuestion(PetSession session,JsonObject result)
    {
        static string Question(string text) {
            var match=Regex.Match(text,@"[^.!?\n]*[?？]\s*$");
            return Regex.Replace(match.Value.ToLowerInvariant(),@"[^\p{L}\p{N}]","");
        }
        var question=Question(result.Str("reply"));
        var repeatedQuestion=question.Length>0 && session.Messages.Where(x=>x.Role=="assistant").TakeLast(6).Any(x=>Question(x.Text)==question);
        var prior=session.Messages.LastOrDefault(x=>x.Role=="assistant")?.QuickReplies ?? [];
        var repeatedChoices=(result.Array("quickReplies") ?? new JsonArray()).Count(x=>prior.Any(p=>p.Text.Equals(x?.Str("text"),StringComparison.OrdinalIgnoreCase)))>=2;
        if(!repeatedQuestion && !repeatedChoices)return;
        // Keep extracted candidates intact, but never repeat a question the person just answered.
        result["reply"]="Thanks for sharing that with me. You can review what I’ve picked up below, add another habit, or correct anything that doesn’t sound like you.";
        result["quickReplies"]=new JsonArray(
            new JsonObject{["text"]="That's all for now",["intent"]="finish_profile"},
            new JsonObject{["text"]="I'd like to add another habit",["intent"]="respond"},
            new JsonObject{["text"]="I'd like to correct something",["intent"]="respond"});
        result["readyForDraft"]=true;
    }

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

    public static void PresentCandidates(JsonObject result,JsonArray candidates)
    {
        var changes=candidates.OfType<JsonObject>().Select(x=>x.Str("key").Replace('_',' ')+": "+x.Str("value")+(x.Str("condition").Length>0 ? " ("+x.Str("condition")+")":""));
        result["reply"]="I picked up: "+string.Join("; ",changes)+". Review the details below before I remember this for future trips.";
        result["quickReplies"]=new JsonArray(
            new JsonObject{["text"]="I'd like to add another habit",["intent"]="respond"},
            new JsonObject{["text"]="I'd like to correct something",["intent"]="respond"},
            new JsonObject{["text"]="That's all for now",["intent"]="finish_profile"});
    }

    public static void Saved(PetSession session,TravelPreference fact)
    {
        var label=fact.Key.Replace('_',' ');
        var reply=$"Saved — {label}: {fact.Value}. I’ll use this for future trips. Your other preferences stay as they are.";
        session.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",reply,"remembering",[
            new(Guid.NewGuid().ToString("N"),"That's all for now","finish_profile"),
            new(Guid.NewGuid().ToString("N"),"I'd like to add another habit","respond"),
            new(Guid.NewGuid().ToString("N"),"I'd like to correct something","respond")]));
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
