# Spike: Thai (th-TH) localization — Buddhist era and culture leakage

**Date:** 2026-08-22 · **Verdict: GO**

Gating question for the i18n epic: does `th-TH` give Buddhist-era dates under ICU without extra
work, and can a per-request `CurrentCulture` of `th-TH` corrupt the invariant strings the app uses
as identifiers (day keys in URLs, `<input type="month">` values, posted decimals)?

## What was probed

A throwaway console app on this machine's .NET 10 / ICU, formatting a fixed date under both
cultures and re-checking invariant parse/format with `CurrentCulture = th-TH`.

| | `en-US` | `th-TH` |
|---|---|---|
| default calendar | `GregorianCalendar` | **`ThaiBuddhistCalendar`** |
| `ddd d MMM yyyy` | `Sat 22 Aug 2026` | `ส. 22 ส.ค. 2569` |
| `d MMMM yyyy` | `22 August 2026` | `22 สิงหาคม 2569` |
| `MMMM yyyy` | `August 2026` | `สิงหาคม 2569` |
| `HH:mm` | `19:30` | `19:30` |
| `1234.5` as `N1` | `1,234.5` | `1,234.5` |
| native digits | `0123456789` | `0123456789` |

## Findings

1. **Buddhist era is free.** `th-TH` resolves to `ThaiBuddhistCalendar`, so year output is AD + 543
   with no calendar wiring of our own. (A Gregorian-year Thai label is also available by cloning the
   culture and swapping in `new GregorianCalendar()` — not what we want, recorded for completeness.)
2. **Digits and separators do not change.** Thai uses Arabic numerals and the same `,`/`.`
   grouping, so quantities, weights and times need no special handling.
3. **The era leaks through any implicit format.** Under `CurrentCulture = th-TH`:
   - `day.ToString("yyyy-MM-dd")` → `2569-08-22`
   - `$"{day:yyyy-MM-dd}"` → `2569-08-22`
   - `day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` → `2026-08-22` ✅

   A day key formatted without an explicit culture would produce `/day/2569-08-22`, which
   `AppTime.TryParseDay` (invariant, `ParseExact`) then rejects — a 404 on every navigation.
4. **Nothing in the codebase does that today.** A sweep for `ToString("…yyyy…")` without
   `InvariantCulture`, for interpolated date formats in `.cshtml`, and for raw date rendering in
   views returned no hits: `AppTime`, `ReportRules` and the input-value helpers all pass
   `InvariantCulture` explicitly. Invariant parsing of `"2026-08-22"` and of the decimal `"2.5"`
   both still succeed with `CurrentCulture = th-TH`.

## Consequences for the build

- Display formatting becomes culture-aware **deliberately and in one place**; the identifier-shaped
  helpers (`AppTime.Key`, `AppTime.InputValue`, `ReportRules` month keys) stay invariant and must
  never be routed through the display path.
- Unit tests for display helpers must pin the culture explicitly rather than relying on the ambient
  one, or they pass or fail depending on the machine.
- Worth a guard test: no `.cs` formats a date without an explicit `CultureInfo`, in the same spirit
  as the native-`confirm()` guard.
- Thai text carries above- and below-line tone marks and vowels, so `leading-none` (used on several
  buttons) risks clipping them. Line height needs revisiting where it is currently pinned, and
  `<html lang>` must reflect the active culture.
