# English app presentation

This branch presents the app in English, including devices that previously saved a Chinese interface setting. It covers iOS and Android navigation, onboarding, profile and trip editing, Koko's application-owned dialogue and choices, planning output, risk and weather messages, setup pages, and bundled place and ticket descriptions.

The change preserves the existing koala assets and ten event/action mappings. English walking and rain choices now trigger the same playful reactions as Chinese choices. Consent, itinerary calculations, booking claims and stored preference identifiers keep their existing meaning.

## Responsibility and data flow

| Area | Owner files | Behaviour |
| --- | --- | --- |
| Native language | `ios/SydneyBuddy/AppLanguage.swift`, `android/.../AppLanguage.kt` | Select English even when legacy settings contain a Chinese code. Native controls use the English locale. |
| Shared UI copy | `shared/localization/ui.json`, `scripts/generate_localizations.py` | Provide the shared catalog, validate English text and placeholders, and generate native resources. Some Chinese source strings remain stable lookup keys. |
| Koko requests | `ios/SydneyBuddy/PetChat.swift`, `Services/PetConversation.cs`, `Services/PetModel.cs` | Send and enforce English reply language independently of travel preferences and memory consent. |
| Fixed dialogue | `Services/ProfilingDialogue.cs`, `Services/KokoCompanion.cs`, `Services/TravelProfiling.cs` | Own English replies, choices, prompts and preference labels. Known legacy values are translated for display. |
| Saved drafts | `Services/PetEnglishCopy.cs` | Recognise previously shipped canned assistant copy on load. Keep message/option IDs, user messages, facts and evidence. |
| Planning and services | `server-dotnet/SydneyBuddy.Api/Planning`, `Services`, `Api` | Return English messages and request English provider results. Existing error/fallback classifications are kept consistent with the translated text. |
| Bundled content | `server/places.json`, `shared/activity-offers.json` | Translate display prose while retaining IDs, coordinates, prices, dates, aliases and internal category/tag values. |
| Reactions | `ios/SydneyBuddy/KoalaAnimation.swift` | Recognise English step/rain choices and refusals without changing the animation state machine. |

The main path is: native selection or typed input → existing API request → English Koko prompt or deterministic response → unchanged travel facts and consent processing → English response → native display and existing animation event.

## Compatibility boundaries

User-authored text, evidence quotes and proper names are not automatically rewritten. Chinese input recognition, old category codes and legacy translation catalogs remain so existing profiles and saved data continue to work. Unknown historical free-form AI prose and descriptions supplied by an existing external dataset are not retroactively machine-translated; new AI turns and supported provider requests use English. Chinese text entered as a user's fact is intentionally retained.

The feature does not migrate or delete profile, trip or memory records, add a service dependency, change the koala artwork, or fix the unrelated budget/negation issues reported by the API audit branch.

## Validation and review

- The complete .NET suite passed locally: **425/425**, using an isolated PostgreSQL-compatible PGlite database with serial execution. Native PostgreSQL validation also runs in backend CI.
- The Python planner, weather and English-presentation checks passed: **20/20**.
- Official H-MEM conformance passed: **13/13**.
- Shared catalog generation and format-token checks pass for **3,545 entries**. The koala bundle still validates **10 actions and 276 frames**.
- Android build, unit tests and lint run through the existing Android workflow. This Linux workspace has no Android SDK and could not download the Gradle distribution.
- The new iOS workflow builds and tests the real app on an available iPhone simulator. This Linux workspace has no Xcode or Swift compiler. CI results are authoritative for native execution; visual checks on a teammate's device are still useful for long English labels.

Review `AppLanguage` first for saved-setting behaviour, `PetResponseLanguage` for the model boundary, `PetEnglishCopy.Refresh` for saved-draft compatibility, and `KoalaConversationFeedback.event` for reactions and negations.

If a Chinese label appears, first identify whether it is a source key missing a catalog entry, a deterministic backend message, a saved historical value, or user/provider content. Add UI mappings in the shared catalog and regenerate resources with `python3 scripts/generate_localizations.py`; do not replace internal category identifiers. For Koko output, confirm both client and backend use this branch and restart the backend after updating it.

On a teammate's computer, fetch and switch to `codex/english-app-20261026`, then follow the repository's normal backend and native app startup steps. Check an existing installation, onboarding, new trip choices, preference confirmation, weather/budget/fatigue messages, and a saved-draft resume. Confirm that “15,000 steps” and accepting an umbrella still animate, while a refusal does not. Main remains unchanged.
