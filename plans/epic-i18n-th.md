# Epic: Thai language support with a toggle

**bd epic:** `med-history-4ei.31` · **Spike:** [`docs/spike-i18n-th.md`](../docs/spike-i18n-th.md) (GO)

Two languages, English default: `en-US` and `th-TH`. A toggle in the nav switches them, the choice
persists, and every app-authored string follows. Data the user typed — entry notes, entry type
names, med names — is never translated.

## Frozen decisions (user, 2026-08-22)

| Decision | Choice | Why it matters |
|---|---|---|
| String storage | `.resx` + `IStringLocalizer` / `IViewLocalizer` | Framework path: fallback, tooling and DataAnnotations localization come free |
| Persistence | Culture **cookie** (`.AspNetCore.Culture`) + `RequestLocalizationMiddleware` | Server renders the right language on first paint — no flash, and print output is correct |
| Dates | Full `th-TH`, **Buddhist era** (2569) | What a Thai reader and a Thai doctor expect on the printed report |
| Scope | All app-authored UI incl. reports, validation and the doctor print | The printed page is the one an outsider reads |

## Scope

25 views / ~2300 lines, plus user-facing strings in 9 `.cs` files.

| # | bd | Slice | Notes |
|---|---|---|---|
| 1 | 4ei.31.1 | Localization infrastructure + culture toggle | `AddLocalization`, `RequestLocalizationOptions` (cookie provider, `en-US` default), pure `CultureRules`, `POST /culture` reusing `RedirectRules` for the return hop, nav toggle rendered twice like the theme toggle, `<html lang>` |
| 2 | 4ei.31.2 | Culture-aware date/time display | Display helpers take the active culture; `AppTime.Key` / `InputValue` / month keys stay invariant. Guard test against implicit date formatting |
| 3 | 4ei.31.3 | Layout, nav, login, error + `SharedResource` baseline | The shared vocabulary (Save, Cancel, Delete, Edit, Today…) that later slices reuse |
| 4 | 4ei.31.4 | Day page: entries, weight, anxiety, med checklist cards | Includes the `data-confirm` copy |
| 5 | 4ei.31.5 | Meds (checklist, stock) and entry types | |
| 6 | 4ei.31.6 | Reports (hub, type, med, weight, anxiety, doctor), history, search | Largest slice; doctor report is the print path |
| 7 | 4ei.31.7 | Server-side strings: DataAnnotations, `ModelState` errors, controller/query labels | `AddDataAnnotationsLocalization` |
| 8 | 4ei.31.8 | Thai typography pass | Tone marks clip under `leading-none`; line-height and tap targets revisited under `lang="th"` |

## Build order

1. `.31.1` alone — every later slice needs the localizer and a way to see Thai.
2. `.31.2` alone — dates are used by nearly every view; landing them before the string slices avoids
   re-touching the same lines.
3. `.31.3` alone — establishes the shared resource other slices key against.
4. `.31.4` `.31.5` `.31.6` may run in parallel (separate worktrees): they touch disjoint views and
   each owns its own per-view `.resx`, so only `SharedResource.th.resx` is a shared file — additions
   there must stay append-only to keep merges trivial.
5. `.31.7` then `.31.8` last; the typography pass wants real Thai copy on the page to judge against.

## Invariants this epic must not break

- Identifier-shaped strings stay invariant: `/day/<key>`, `<input type="month">`, `<input
  type="datetime-local">`, posted decimals. Under `th-TH` an implicit format yields `2569-…` and a
  404 — see the spike.
- The theme toggle exists twice (desktop + mobile); the culture toggle follows the same rule and is
  bound by class, never by id.
- The culture endpoint must be `[AllowAnonymous]` — the login screen needs the toggle too.
- Translated copy is app-authored text only. Entry types and med names are user data.

## Open

- Thai copy is drafted by the build and needs a native read-through before it is considered final;
  medical wording (dose, slot, allocation, tick) is the risky vocabulary.
