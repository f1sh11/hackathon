# Personal profile agent conversation

## Behaviour

The iOS first introduction and Settings > personal preferences use the same Koko conversation instead of the fixed five-question form. The model generates the opening and every subsequent utterance together with exactly three contextual reply options. Optional free text remains visible. The personal scene stacks the three reply buttons vertically.

This first agent learns usual preferences and conditions. Its prompt asks for natural, relevant conversation: respond to the person, then follow connections that can help future travel recommendations. It does not collect this trip's destination, dates, party size or budget. Those remain in the separate trip preparation flow.

The backend validates three distinct options before applying or saving a turn. Invalid/missing options preserve the last valid snapshot and return a retryable error. Successful turns retain question/option evidence and existing request/choice replay behaviour. Closing Settings returns to Settings; finishing the first introduction opens trip preparation.

## Responsibility map

| File | Responsibility |
| --- | --- |
| `server-dotnet/SydneyBuddy.Api/Prompts/PersonalProfile.md` | Editable runtime instructions for the first agent, embedded into the API assembly |
| `Services/PersonalProfileAgent.cs` | Personal mode context, exact-three reply validation, opening and turn completion |
| `Services/PetModel.cs` | Existing NVIDIA transport; selects the dedicated prompt/context for personal sessions |
| `Api/PetEndpoints.cs` | Personal session creation and isolated turn handling; reuses evidence, retry and memory APIs |
| `Services/TravelProfiling.cs` | Evidence-backed personal candidates from general habit choices; protects scope and explicit numerical evidence |
| `ios/SydneyBuddy/PetChat.swift` | Personal session scope, three generated replies, free input, recovery and entry/exit behaviour |
| `ios/SydneyBuddy/KokoDialogue.swift` | Shared option renderer, including stacked personal replies |

Flow: personal screen -> session with `purpose=personal_profile` -> dedicated model instructions -> validated utterance and three replies -> scoped candidate extraction -> optional memory confirmation -> existing memory persistence. A finished introduction is not evidence of a successful memory write or a complete trip.

Use a new `petSession.personalProfileAgent` device key; retain the old `personalProfile` session and all saved memory/trips. Existing trip clients and legacy dialogue mode remain available. General personal choices can create unconfirmed long-term candidates; they no longer automatically become activity-only trip preferences. Temporary requirements and inferences do not become personal memory candidates. An unsupported numerical walking limit is rejected.

## Validation boundaries

Added backend checks exercise the real model SSE/JSON parser with an isolated transport response, and real HTTP/PostgreSQL flows with a model double. They cover opening/options, point selection and free text, retry/resume, stale choice handling, invalid option sets, trip-field isolation, consent, recall and refusal. Added iOS regression checks cover purpose/version, no trip defaults, exactly three restored options and no second opening on resume.

Localisation/catalog consistency and the four existing English guard tests pass locally. The local environment has no .NET SDK or Xcode; native backend/iOS build and test results must be taken from the branch's CI. Do not treat fixture dialogue as a live-model quality result.

CI also exposed an inherited `GuidedTripDetails` build error: `BudgetAmountTextField` does not expose the assigned `allowsNumberPadPopover` member to the runner's Swift compiler. Remove that optional assignment; retain the decimal keyboard and existing budget input/normalisation rules.

After that fix, the runner also timed out type-checking the inherited `SavedTripWorkspace.body`. Extract its existing day tabs, map, panel header, transport, schedule, proposal and presentation expressions into opaque view helpers. Preserve the source strings, layout order, callbacks and data flow; this only reduces the compiler's expression workload.

Update the backend and iOS client together. Runtime wording and pacing still need a configured NVIDIA conversation and hands-on device review. In that review, try an anime interest followed by a condition about exhibitions, reject a proposed inference, type a correction, then reopen the conversation. Check that the dialogue feels natural and stays useful for the personal travel profile, with three relevant options after every successful response.

## Review and debugging

Read the prompt first to change conversational behaviour. Read the personal branch in `PetEndpoints` to check mode isolation and validation before mutation. If replies are missing, inspect the model response and `PersonalProfileAgent.Replies`, then the client's `contextualReplies`; do not replace a valid response with a fixed questionnaire. If memory is wrong, inspect the original question, selected option, candidate scope and consent result before inspecting the planner.
