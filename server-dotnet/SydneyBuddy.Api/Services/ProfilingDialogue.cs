using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SydneyBuddy.Api;

public sealed record PetQuickReply(string Id,string Text,string Intent="respond");
public sealed record PetChoiceEvidence(string QuestionMessageId,string QuestionText,string OptionId,string OptionText);
public sealed record TravelInspiration(string Title,string[] Ideas,string Status="inspiration",bool PlacesVerified=false);

// Dialogue completion and long-term memory consent are deliberately independent.
public sealed class ProfilingDialogue
{
    public int Version { get; set; } = 2;
    public int CompanionVersion {get;set;}
    public bool MoodAnswered {get;set;}
    public bool RecallOffered {get;set;}
    public string RecallKey {get;set;}="";
    public string RecallValue {get;set;}="";
    public string RecallSourceId {get;set;}="";
    public int ResponseCount { get; set; }
    public string Phase { get; set; } = "introducing";
    public string Nickname { get; set; } = "";
    public string[] Ideas { get; set; } = [];
    public int IdeaRevision { get; set; }
    public string ProposedDirection { get; set; } = "";
    public string RequestedField {get;set;}="";
    public string SelectedDirection { get; set; } = "";
    public TravelInspiration? Inspiration { get; set; }
}

public static class KokoDialogue
{
    public const int MaximumResponses=4;
    public const string Prompt="""
You are Koko, a friendly, proactive and dependable koala travel guide with a sense of boundaries. dialogue tracks the first profiling conversation; currentChoice is the user's selection in response to the preceding proposal, not an isolated statement.
The application handles the optional name on first meeting. Use a known nickname naturally; do not introduce yourself again or ask for it again. Never obtain a user's name from examples, documents or guesses.
Imagine the trip together before departure: "for this trip", "when we get there", "if there is one along the route", "we could start with...". Usually use one or two sentences, make one current decision, contribute a concrete experience and follow the user's choice. If the user declines, offer another idea without persuasion or demanding a reason. Avoid repeated acknowledgements, paraphrases, cuteness or praise. Never invent personal experiences or claim to have seen a work.
Extend the existing JSON protocol with quickReplies:[{"text":"short natural reply","intent":"respond or skip or draft"}], ideas:["concrete trip ideas as they develop"], and readyForDraft:bool.
During the introductory conversation, usually offer three clickable replies for one decision, including an exit to decline, change the idea or skip. Do not preselect or mark recommendations. A button must fully answer the question without requiring typing. An interest entry must include a way to skip, not trap the user in an endless cycle of alternatives. Adapt question order to known information instead of traversing fields or twelve dimensions in a fixed order.
dialogue.responsesRemaining includes the current answer. When mustDraft=true, or the user asks to hurry, stop asking or plan directly, immediately produce an inspiration draft from known information, append no further profiling questions to reply, and set readyForDraft=true. Finish earlier when enough is known. The optional name counts towards the four-response maximum; preference confirmations are optional cards. Show how earlier choices have shaped the emerging plan.
Unknown destination, date, budget or party size must not block inspiration; keep them unknown without defaulting to Sydney, one day, one person, a budget or existing bookings. Inspiration is not a formal timetable, quote or booking. This conversation has no place-verification tools: ideas must be explicitly conditional possibilities, never claims that a venue exists, is open, a price or service is real, or weather has been verified. Say details will be checked later when needed.
ideas are Koko's proposals, not user preferences. A currentChoice applies only to the scene in questionText. Extract it with scope=activity, retain the full applicable activity and conditions in condition, and quote optionText verbatim as evidence. "If it's on the way" does not imply a dedicated visit or purchase; a leisurely meal does not imply a leisurely pace for the whole trip.
Explicit everyday habits can become long_term candidates, requirements for this trip use trip, and specific activities use activity. AI guesses remain inferred. Every long-term candidate requires an optional memory-card confirmation; never claim it is saved because of a click, high confidence or simulated consent.
Inference limits: staying home or liking anime does not establish low fitness, introversion or familiarity with a particular work. Waiting 20–30 minutes for a restaurant does not establish an exact limit or tolerance for every queue. Getting up once for a worthwhile view does not establish a normal waking schedule. Avoiding evening harbour sun does not imply intolerance of heat or cold. Accepting a stroll does not establish a walking limit or hill fitness. Accepting buses or light rail does not establish a transfer limit or taxi budget. Unreported dietary restrictions do not mean allergies have been ruled out. Leave unsupported dimensions unknown; do not place them in fields to bypass preference-evidence requirements.
Never take nickname, ideas or quickReplies from simulated users in product documents. Never prefill simulated preferences. Once in inspiration, continue refining or discussing the draft without restarting required profiling. Use the shared trip-details entry for facts required by formal planning.
""";

    public static void Enable(PetSession session,JsonObject input)
    {
        if(input.Int("profilingVersion")!=2)return;
        if(session.Dialogue is not null){if(input.Int("companionVersion")==1)session.Dialogue.CompanionVersion=1;return;}
        var name=input.Str("nickname").Trim();
        if(name.Length>24 || name.Any(char.IsControl))name="";
        var previousResponses=session.Messages.Count(x=>x.Role=="user");
        session.Dialogue=new(){Nickname=name,ResponseCount=Math.Min(previousResponses,MaximumResponses),CompanionVersion=input.Int("companionVersion")==1 ? 1:0};
        if(previousResponses>0) {
            session.Dialogue.Phase="exploring";
            if(previousResponses>=MaximumResponses)Finish(session,[]);
            // Upgrading a saved draft preserves every prior answer and never repeats the introduction.
            session.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",
                session.Dialogue.Inspiration is null ? "Let's build on your ideas. Shall we leave time to explore, or start with one experience you're looking forward to?":"Let's turn your ideas into a direction for the trip. We can add the details together later.",
                "curious",session.Dialogue.Inspiration is null ? ExplorationReplies():DraftReplies()));
            return;
        }
        var returning=input.Bool("hasMetKoko") || name.Length>0;
        if(returning)session.Dialogue.Phase="exploring";
        // A legacy empty draft only contains the old welcome. Replace it rather than introducing twice.
        if(session.Messages.All(x=>x.Role=="assistant"))session.Messages.Clear();
        var greeting=returning
            ? (name.Length>0 ? $"{name}, shall we start with a nature stroll or the city's stories? You can suggest something else too.":"Shall we start with a nature stroll or the city's stories? You can suggest something else too.")
            : "Hi, I'm Koko, your travel guide. What should I call you? You can skip this too.";
        session.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",KokoCompanion.Enabled(session) && returning ? KokoCompanion.Greeting:greeting,"greeting",KokoCompanion.Enabled(session) && returning ? KokoCompanion.MoodReplies():returning ? ExplorationReplies():NameReplies()));
    }

    public static PetQuickReply? ResolveChoice(PetSession s,JsonObject input)
    {
        s.CurrentChoice=null;
        if(input.Str("optionId").Length==0 && input.Str("questionId").Length==0)return null;
        var question=s.Messages.LastOrDefault(x=>x.Role=="assistant");
        var option=question?.QuickReplies?.FirstOrDefault(x=>x.Id==input.Str("optionId"));
        if(s.Dialogue is null || question?.Id!=input.Str("questionId") || option is null || option.Intent is "compose_name" or "details")
            throw new ServiceException("These options have changed. Choose a current option, or tell Koko directly.");
        if(input.Str("field").Length>0 || (input.Str("message").Trim()!=option.Text && PetEnglishCopy.Translate(input.Str("message").Trim())!=option.Text))
            throw new ServiceException("That selection does not match the current suggestion. Refresh and try again.");
        s.CurrentChoice=new(question!.Id,question.Text,option.Id,input.Str("message").Trim());
        return option;
    }

    public static bool Expedite(string message)=>new[]{"快点","别问了","不要再问","直接安排","直接给我草案","先看草案","hurry","stop asking","no more questions","just plan","show me a draft","show the draft"}.Any(x=>message.Contains(x,StringComparison.OrdinalIgnoreCase));

    // Server-owned shortcuts never require a model call or a name to proceed.
    public static JsonObject? DirectResponse(PetSession s,string text,PetQuickReply? choice)
    {
        if(s.Dialogue is not {} d)return null;
        if(KokoCompanion.Direct(s,text,choice) is {} companion)return companion;
        if(choice?.Intent=="choose_sydney") {s.Fields["destination"]="悉尼";return StepResult(s,"Sydney it is. I'll keep the rest of your trip details.", "destination");}
        if(choice?.Intent is "people_1" or "people_2") {s.Fields["travelers"]=choice.Intent[^1..];return StepResult(s,"I've noted how many people are coming.", "travelers");}
        if(choice?.Intent is "budget_60" or "budget_120") {
            s.Fields["budget"]=choice.Intent[7..];s.Fields["budgetCurrency"]="AUD";s.Fields["budgetBasis"]="perPersonPerDay";s.Fields["budgetCoverage"]="activities";d.RequestedField="";
            return StepResult(s,"For this trip, I'll use "+s.Fields["budget"]+" AUD per person per day for activities. Accommodation, transport and the full meal budget are separate.", "budget");
        }
        if(choice?.Intent=="defer_time") {s.Fields.Remove("date");d.RequestedField="time";return StepResult(s,"We'll leave the date open and keep your draft. Choose a date when you're ready to generate the itinerary.", "time");}
        if(BudgetQuestion(text)) {
            s.Fields.Remove("budget");d.RequestedField="budget";
            return new(){["reply"]="I'll set the previous amount aside. You can start with 60 or 120 AUD per person per day for activities and adjust it later. These are budget options, not verified costs; accommodation, transport and the full meal budget are separate.",["readyForDraft"]=true,["followTripStep"]=true};
        }
        if(choice?.Intent=="change_direction" || text is "换个思路吧" or "换个想法吧" or "换一组想法" || text.Equals("Try another idea",StringComparison.OrdinalIgnoreCase)) {
            var current=Array.IndexOf(DirectionKeys,d.ProposedDirection);
            return Propose(s,DirectionKeys[(current+1+DirectionKeys.Length)%DirectionKeys.Length],false);
        }
        if(choice?.Intent is "direction_nature" or "direction_culture" or "direction_food")return Propose(s,choice.Intent[10..],true);
        if(choice?.Intent=="accept_direction")return Propose(s,d.ProposedDirection.Length>0 ? d.ProposedDirection:DefaultDirection(s),true);
        if(choice?.Intent=="keep")return d.ProposedDirection.Length>0 ? Propose(s,d.ProposedDirection,true):Propose(s,DefaultDirection(s),false);
        if(choice?.Intent=="draft" || Expedite(text)) {
            if(d.Ideas.Length==0){var proposed=Propose(s,DefaultDirection(s),false);proposed["reply"]="Here's a first "+DirectionTitle(d.ProposedDirection)+" idea. We can add the trip details next.";return proposed;}
            return new(){["reply"]="Let's sketch a draft from what we know. We can fill in the missing details together later.",["readyForDraft"]=true};
        }
        if(text=="留一段自由探索" || text.Equals("Leave time to explore",StringComparison.OrdinalIgnoreCase))return Propose(s,"nature",false);
        var match=Regex.Match(text,@"^(?:叫我|我叫|称呼我|call me\s+|my name is\s+)\s*([^,，。.!！？\r\n]{1,24})[。.!！]?$",RegexOptions.IgnoreCase);
        // A single name in direct response to the optional name question is explicit evidence.
        var shortName=d.Phase=="introducing" && choice is null && Regex.IsMatch(text,@"^[A-Za-z][A-Za-z'’-]{0,23}$") &&
            !new[]{"skip","hello","hi","yes","no","unknown"}.Contains(text,StringComparer.OrdinalIgnoreCase);
        if(choice?.Intent=="use_alias")d.Nickname="Travel buddy";
        else if(match.Success)d.Nickname=match.Groups[1].Value.Trim();
        else if(shortName)d.Nickname=text;
        if(d.Phase!="introducing")return null;
        if(choice?.Intent is not ("skip_name" or "use_alias") && !match.Success && !shortName)return null;
        if(KokoCompanion.Enabled(s))return new(){["reply"]=KokoCompanion.Greeting,["companionTurn"]=true,["companionReplies"]=System.Text.Json.JsonSerializer.SerializeToNode(KokoCompanion.MoodReplies(),JsonExtensions.Options)};
        return new(){["reply"]=(d.Nickname.Length>0 ? $"{d.Nickname}, let's find a little inspiration for this trip. ":"Let's find a little inspiration for this trip. ")+"Would you prefer a nature stroll or the city's stories? We can try another idea too.",
            ["offerDirections"]=true};
    }

    public static PetMessage CompleteTurn(PetSession s,JsonObject result,string reply,string mood)
    {
        if(s.Dialogue is not {} d)return new(Guid.NewGuid().ToString("N"),"assistant",reply,mood);
        if(result.Bool("companionTurn")){d.ResponseCount=Math.Min(MaximumResponses,d.ResponseCount+1);d.Phase="exploring";return new(Guid.NewGuid().ToString("N"),"assistant",reply,"relaxed",KokoCompanion.Options(result));}
        var initial=d.Inspiration is null;
        if(initial)d.ResponseCount=Math.Min(MaximumResponses,d.ResponseCount+1);
        var ideas=(result.Array("ideas") ?? new JsonArray()).OfType<JsonValue>()
            .Select(x=>x.TryGetValue<string>(out var t) ? t.Trim():"").Where(x=>x.Length is >0 and <=240).Distinct().Take(3).ToArray();
        if(ideas.Length>0)d.Ideas=ideas;
        var hasTripFacts=s.Fields.Any(x=>PetQuestions.Required.Contains(x.Key) && PetQuestions.Known(x.Value));
        var finish=d.Inspiration is not null || d.ResponseCount>=MaximumResponses || result.Bool("readyForDraft") || hasTripFacts;
        PetQuickReply[] options;
        if(finish) {
            Finish(s,d.Ideas);
            // The fourth answer ends the initial interview even when the model asks another question.
            if(initial && !result.Bool("directionChanged") && !result.Bool("followTripStep")) {
                if(hasTripFacts){if(string.IsNullOrWhiteSpace(reply) || reply.Contains('？') || reply.Contains('?'))reply="I've kept your trip details.";reply+="\n"+StepQuestion(s);}
                else reply="Here's a starting direction we can shape together. Once the trip details are ready, we'll check specific places, times and costs.";
            }
            options=DraftReplies(s);
        } else {
            d.Phase="exploring";
            var proposed=(result.Array("quickReplies") ?? new JsonArray()).OfType<JsonObject>().Take(4)
                .Where(x=>x.Str("text").Length is >0 and <=45)
                .Select(x=>new PetQuickReply(Guid.NewGuid().ToString("N"),x.Str("text"),x.Str("intent") is "draft" or "skip" ? x.Str("intent"):"respond"))
                .DistinctBy(x=>x.Text).ToArray();
            if(result.Bool("offerDirections"))options=ExplorationReplies();
            else if(proposed.Length is >=3 and <=4 && proposed.Any(x=>x.Intent is "skip" or "draft"))options=proposed;
            else {
                // Safe generic reactions bind to the actual proposal; no unrelated field questionnaire.
                options=[Reply("Let's try this idea"),Reply("Try another idea","skip"),Reply("Show the draft so far","draft")];
                if(string.IsNullOrWhiteSpace(reply))reply="Shall we leave some time to wander, then build around the experience you're most looking forward to?";
            }
        }
        if(result.Bool("followTripStep")){reply=result.Str("reply");options=DraftReplies(s);}
        else if(finish && result.Bool("directionChanged") && d.SelectedDirection==d.ProposedDirection)reply+="\n"+StepQuestion(s);
        return new(Guid.NewGuid().ToString("N"),"assistant",reply,mood,options);
    }

    private static void Finish(PetSession s,string[] ideas)
    {
        var d=s.Dialogue!;d.Phase="inspiration";
        if(ideas.Length==0){d.ProposedDirection=DefaultDirection(s);ideas=DirectionIdeas(s,d.ProposedDirection);}
        d.Ideas=ideas;
        d.Inspiration=new(PetQuestions.Known(s.Fields.GetValueOrDefault("destination")) ? (PetQuestions.SupportedDestination(s.Fields["destination"]) ? "Sydney":s.Fields["destination"])+" · "+DirectionTitle(d.ProposedDirection):"Destination to decide · "+DirectionTitle(d.ProposedDirection),ideas);
    }
    private static PetQuickReply Reply(string text,string intent="respond")=>new(Guid.NewGuid().ToString("N"),text,intent);
    private static PetQuickReply[] NameReplies()=>[Reply("Skip for now","skip_name"),Reply("Call me Travel buddy","use_alias"),Reply("I'll enter a name","compose_name")];
    private static PetQuickReply[] ExplorationReplies()=>[Reply("Nature stroll","direction_nature"),Reply("City stories","direction_culture"),Reply("Try another idea","change_direction")];
    public static bool BudgetQuestion(string message)=>Regex.IsMatch(message,"预算|budget",RegexOptions.IgnoreCase) && !Regex.IsMatch(message,@"\d") && Regex.IsMatch(message,"没想好|不确定|大概范围|推荐|建议|not sure|unsure|undecided|recommend|suggest|rough range",RegexOptions.IgnoreCase);
    public static string NextNeed(PetSession s) {
        if(s.Dialogue?.RequestedField=="budget" && !PetQuestions.Answered(s,"budget"))return "budget";
        if(!PetQuestions.Answered(s,"destination"))return "destination";
        if(s.Dialogue?.RequestedField!="time" && (!PetQuestions.Answered(s,"date") || !PetQuestions.Answered(s,"days")))return "time";
        if(!PetQuestions.Answered(s,"travelers"))return "travelers";
        if(!PetQuestions.Answered(s,"budget") || !PetQuestions.Answered(s,"budgetCurrency") || !PetQuestions.Answered(s,"budgetBasis"))return "budget";
        if(!PetQuestions.Answered(s,"date") || !PetQuestions.Answered(s,"days"))return "time";
        return "";
    }
    public static string StepQuestion(PetSession s)=>NextNeed(s) switch {
        "destination"=>PetQuestions.UnsupportedDestination(s) ? "I'll keep your details. Routes are currently available for Sydney only; switch to Sydney or keep this draft for later.":"Which city would you like to visit? I can plan Sydney routes, or save ideas for other cities for later.",
        "time"=>"Next, choose a departure date and trip length, up to 15 days. You can keep the draft if the date is still open.",
        "travelers"=>"How many people are coming, including you?",
        "budget"=>"Choose 60 or 120 AUD per person per day for activities, or set your own amount and scope. Accommodation, transport and the full meal budget are separate.",
        _=>"Your trip details are ready. Generate the itinerary, keep chatting or change a detail whenever you like."
    };
    private static JsonObject StepResult(PetSession s,string text,string focus)=>new(){["reply"]=text+"\n"+StepQuestion(s),["readyForDraft"]=true,["followTripStep"]=true};
    public static void DetailsUpdated(PetSession s,JsonObject changes) {
        if(s.Dialogue is null || !changes.Any(x=>PetQuestions.Required.Contains(x.Key) || new[]{"budgetCoverage","origin","mustDo","avoid","companionNeeds"}.Contains(x.Key)))return;
        if(PetQuestions.Answered(s,"budget"))s.Dialogue.RequestedField="";
        if(s.Dialogue.Inspiration is null)Finish(s,[]);
        var labels=new Dictionary<string,string>{{"destination","Destination"},{"date","Date"},{"days","Trip length"},{"travelers","Party size"},{"budget","Budget"},{"origin","Starting point"},{"mustDo","Must-do plans"},{"avoid","Things to avoid"},{"companionNeeds","Companion needs"}};
        var names=string.Join(", ",labels.Where(x=>changes.ContainsKey(x.Key)).Select(x=>x.Value));
        var text=(names.Length>0 ? names+" updated.":"Your trip details are updated.")+"\n"+StepQuestion(s);
        s.Messages.Add(new(Guid.NewGuid().ToString("N"),"assistant",text,"curious",DraftReplies(s)));
        if(s.Messages.Count>100)s.Messages=s.Messages.TakeLast(100).ToList();
    }
    private static PetQuickReply[] DraftReplies(PetSession? s=null) {
        if(s is null)return [Reply("Add trip details","details"),Reply("Try another idea","change_direction"),Reply("I'll write my own","compose_idea")];
        if(s.Dialogue is {} d && d.ProposedDirection.Length>0 && d.SelectedDirection!=d.ProposedDirection && d.RequestedField.Length==0 && !s.Fields.Keys.Any(PetQuestions.Required.Contains))return [Reply("Choose this direction","accept_direction"),Reply("Try another idea","change_direction"),Reply("I'll write my own","compose_idea")];
        return NextNeed(s) switch {
            "destination"=>PetQuestions.UnsupportedDestination(s) ? [Reply("Switch to Sydney","choose_sydney"),Reply("Change city","details_destination"),Reply("Keep draft","keep_draft")]:[Reply("Choose Sydney","choose_sydney"),Reply("Another city","details_destination"),Reply("I'll write my own","compose_idea")],
            "time"=>[Reply("Choose date and length","details_time"),Reply("Date still open","defer_time"),Reply("I'll write my own","compose_idea")],
            "travelers"=>[Reply("1 person","people_1"),Reply("2 people","people_2"),Reply("Another party size","details_people")],
            "budget"=>[Reply("60 AUD / person / day","budget_60"),Reply("120 AUD / person / day","budget_120"),Reply("Set my own budget","details_budget")],
            _=>[Reply("Edit trip details","details"),Reply("Try another idea","change_direction"),Reply("I'll write my own","compose_idea")]
        };
    }
    private static readonly string[] DirectionKeys=["nature","culture","food"];
    private static string DefaultDirection(PetSession s)=>Regex.IsMatch(s.Fields.GetValueOrDefault("interests",""),"文化|艺术|culture|art",RegexOptions.IgnoreCase) ? "culture":Regex.IsMatch(s.Fields.GetValueOrDefault("interests",""),"美食|food",RegexOptions.IgnoreCase) ? "food":"nature";
    private static string DirectionTitle(string key)=>key switch {"nature"=>"Nature stroll","culture"=>"City stories","food"=>"Neighbourhood food stroll",_=>"Trip inspiration"};
    private static string[] DirectionIdeas(PetSession s,string key)=>key switch {
        "culture"=>["Explore a neighbourhood's stories, architecture and local life. We'll check the actual area later.",Regex.IsMatch(s.Fields.GetValueOrDefault("avoid",""),"博物馆|museums?",RegexOptions.IgnoreCase) ? "Skip museums and leave that time for street scenes, small shops and a rest.":"If there's a suitable exhibition or cultural venue, choose one to explore at your own pace, then take a break. Opening times still need checking."],
        "food"=>["Explore a neighbourhood with plenty of food options. We'll check specific places, opening times and prices later.","Combine a meal with nearby small shops, with breaks between them and no rushing from one stop to another."],
        _=>["If the destination has a suitable park, greenway or waterfront, leave time for an easy stroll. We'll check specific places later.","Take a break when your feet need it, then explore nearby streets in the afternoon. Leave some room in the day."]};
    private static JsonObject Propose(PetSession s,string key,bool accepted) {
        var d=s.Dialogue!;d.ProposedDirection=key;d.IdeaRevision++;d.Ideas=DirectionIdeas(s,key);
        if(accepted){d.SelectedDirection=key;s.Fields["interests"]=key switch {"culture"=>"文化、艺术","food"=>"美食、城市",_=>"自然、风景"};}
        return new(){["reply"]=accepted ? "Let's plan around "+DirectionTitle(key)+" for this trip. This choice applies to this trip only.":"Here's another direction: "+DirectionTitle(key)+". "+d.Ideas[0]+" What do you think?",["ideas"]=new JsonArray(d.Ideas.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["readyForDraft"]=true,["directionChanged"]=true};
    }
}
