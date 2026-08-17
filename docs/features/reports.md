# Reports + search

## Hub

`/report` (`ReportsController`) is a static list of links. The navbar's "Reports ▾" is a pure-CSS `group-hover` flyout — desktop hover opens the menu, a touch tap just follows the link to the hub. No JS. See [navigation.md](./navigation.md).

## Type report

`/type-report[/{type}?page=N]` — `TypeReportController` + pure `TypeReportRules`.

One selector button per `EntryTypes` row, **including inactive ones** (history stays viewable). Entries grouped by local day, newest day first, ascending within a day. Shows severity, note, photo thumbs; day headers link to the day page.

Pagination = 30 **distinct entry-days** per page, out-of-range clamps and redirects. Two queries per page — an `OccurredAt`-only scan, then a day-window fetch. No N+1.

## Med report

`/med-report[/{yyyy-MM}]` — `ReportController` + pure `ReportRules`. Month calendar, Monday-first grid, all meds combined.

Per day it counts ticked/planned slots **bucketed by `allocation.Day`** — a plan-day basis. Consequence, documented on `ReportRules`: the link alone is what counts, so an entry hand-edited onto another date still counts here even though the day page shows that slot unticked.

States: Full (ticked ≥ planned, solid black) / Partial / None / NoPlan.

Slot semantics reuse `ChecklistRules.FindTick` + `MedPlanRules` — duplicate ticks collapse, dangling links are ignored.

## Anxiety report

`/anxiety-report[/{yyyy-MM}]` — see [anxiety.md](./anxiety.md). Built on the same generic `ReportRules.BuildWeeks<TCell>` as the med report.

## Weight report

`/weight-report[/{yyyy-MM}]` — `WeightReportController`. Month calendar, one cell per day, **read-only**: every cell links back to the day page, which is where readings are logged. Data + view-model assembly live in `WeightReportQueries.WeightMonthAsync`; the controller is route parsing and view selection only (same split as `AnxietyReportController`). A garbage month redirects to the current month, like the other reports.

Readings themselves are added and deleted on the day page — `POST /day/{date}/weight` and `/day/{date}/weight/{id}/delete`, value parsed by `MeasurementRules.TryParseValue`, errors surfaced through `TempData["WeightError"]`.

## Doctor export

`/doctor-report?from=&to=` — `DoctorReportController` + pure `DoctorReportRules`.

Printable range summary, day-grouped **ascending** — the only report that reads oldest-first. Per-type totals plus a count of days with an anxiety vote. Photos appear as "(N photos)", never as images.

`ResolveRange` rules:

- any bad bound → the **whole** range defaults to the last 30 days (never a mix of one good and one defaulted bound)
- `from > to` → swap
- span > 366 days → clamp `From` forward (`To` never moves)

Printing works via Tailwind `print:hidden` on the controls and site nav — see [theming.md](./theming.md#print) for the escaped-selector gotcha. Linked from both the Reports dropdown and the hub.

## Search

`/search?q=&page=N` — `SearchController` + pure `SearchRules`.

Case-insensitive `ILIKE` substring over `Note` + `PillName`. The user's `%`, `_` and `\` are escaped to literals — **backslash first**. Empty `q` renders a bare form.

Pagination reuses `TypeReportRules`' distinct-day machinery wholesale: 30 day-blocks, clamp-redirect, the same two-query shape with a match re-filter on the day-window fetch. Navbar link sits after Reports.
