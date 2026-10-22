# Android migration verification

Verified on 22 October 2026 using Windows, JDK 21, Gradle 8.9, Android SDK 35, Python 3.13, Node 22.14 (with experimental TypeScript transformation), and a Pixel 7 emulator running Android 14 / API 34. Node 24 remains the recommended team runtime. The starting `dev` commit was `16e4cca6e878ff65017b612e99bb1f1569457551`.

The Android client uses Kotlin and Jetpack Compose. The Python API and official H-MEM engine remain shared; the iOS directory is retained. Explicit UTF-8 decoding was added to the Python catalog, local configuration, and Node bridge output for Windows compatibility. No provider credentials are committed.

## Automated checks

- `gradlew :app:assembleDebug :app:testDebugUnitTest :app:lintDebug`: passed. Six domain tests cover decimal money conversion, exact remainder allocation, invalid amounts, profile validation, proposal identity/revision handling, and Google polyline decoding. Lint reports no errors; library update suggestions remain.
- `gradlew :app:connectedDebugAndroidTest`: passed on the API 34 emulator. The UI test completes onboarding, generates and saves a demo itinerary, cancels an adjustment draft, opens route details, marks luggage ready, records an expense, verifies the A$10.01 three-person split (3.34 / 3.34 / 3.33), recreates the activity, and saves a second itinerary without overwriting the first. It creates development test records and does not call paid APIs.
- `python -m unittest discover -s server/tests -v`: all 21 tests passed, including HTTP save/revision conflict/deletion, preference consent/retrieval/revocation, invalid inputs, deterministic route constraints, and weather failures.
- Official H-MEM conformance: 13/13 passed on Windows.

The UI test recreates an Activity; it does not simulate an OS process kill. Local preferences use Android SharedPreferences, and saved itineraries are reloaded from the computer backend. Local test data is separate from any deployed service.

## Online checks

An actual NVIDIA assisted request succeeded. An actual live planning request for 24 October 2026 succeeded with four stops after querying Google Places, Routes, and Weather plus NVIDIA Nemotron. The weather response was available for the requested day. Provider inputs used Sydney landmark coordinates and test travel preferences.

The first Google request returned `API_KEY_IP_ADDRESS_BLOCKED`. After the owner explicitly approved removing the server key's IP restriction while retaining the Places / Routes / Weather API restriction, the live request succeeded. This account configuration is not embedded in source code.

Maps SDK for Android was enabled with the owner's approval. A separate Android-only key was created with package and debug-signature restrictions and saved in ignored `android/local.properties`. Google base map tiles and itinerary markers were visually verified in the running emulator. Demo connectors are grey and explicitly labelled as illustrative; they are not presented as real walking routes.

## Running and remaining boundaries

Use `adb reverse tcp:8787 tcp:8787` for both emulator and USB-connected device development. The backend remains bound to `127.0.0.1`; no LAN or public exposure was added. Debug permits cleartext only to loopback hosts, while Release blocks cleartext. Cloud deployment, user authentication, release signing, and store submission remain separate work.

This establishes the tested emulator workflow, not compatibility with every Android manufacturer, screen size, or API level. No physical Android device or macOS build was available for this verification. Teammates must configure their own SDK path and Android map signing fingerprints. The backend holds itineraries and preferences on the computer; expenses and preparation progress remain on each Android device.
