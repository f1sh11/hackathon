# Selective English experience integration

Source: `codex/english-app-20261026` at `0894b92`, adapted onto main `487c354`.
This is a selective integration, not a replacement of the branch's complete app or data files.

## Included

- Server-side English presentation of recognised Koko fixed replies, questions, choices and historical draft copy.
- English unknown-answer and habit recognition, canonical stored energy/pace values, and English refusal handling for preference candidates.
- English walking/rain reactions with negation handling; existing koala artwork and playback remain intact.
- Explicit localisation for dynamic date, day-count, party-size and schedule controls.
- English-catalog and money-format guards, regression checks, and an iOS simulator CI workflow.

## Compatibility boundaries

The interface language remains selectable. English presentation operates on copies and does not rewrite saved assistant messages, user messages, facts, evidence, option IDs or long-term memories. Unknown historical model prose and user-authored proper names remain unchanged. An `X-App-Language` request header selects the display language when opening an existing session; clients without it retain stored session language.

Translated options retain their original IDs. Validation accepts the original stored label or its recognised English presentation when English is selected, while rejecting stale question/option IDs. English `Today` and `Tomorrow` are resolved using Sydney's date. Zero budgets remain valid.

The current Trips layout, unified route workspace, Apple Maps implementation and planning safeguards are retained. Android, demo data, provider integrations and forced English preference migration from the source branch are excluded.

## Validation

- iOS suite: 198 tests passed; final dynamic-label changes also compiled and were checked on iPhone 18 Pro Simulator.
- Backend suite: 446 tests passed, including English choice round-trips, historical-copy preservation, language selection, dates, zero budgets and memory refusals.
- Catalog-guard suite: 4 tests passed; generated resources and workflow YAML checked.
- Manual checks: resumed draft and budget explanation in English; day count updates from 1 to 2 in English; party count in English; cancelled edits preserve the original draft.
- Existing 39 trips and 4 memories compare equal before/after runtime verification.

The GitHub iOS workflow is configured but has not been executed remotely as part of this local integration. Simulator checks do not replace outdoor GPS navigation testing.
