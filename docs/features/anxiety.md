# Anxiety vote + report

## The vote

One editable vote per day. `AnxietyVotes`, unique `Day` index, `AnxietyLevel` = Calm / Ok / Tense / Anxious / Panic, stored as string.

The day page shows a card under the meds checklist. `POST /day/{date}/anxiety/{level}` toggles: **voting the level already set clears the day** — that second tap is the widget's only undo control. The decision is the pure `AnxietyRules.DecideVote`.

A racing first-vote double submit is swallowed by a `DbUpdateException` guard (same pattern as AddStock).

## The report

`/anxiety-report[/{yyyy-MM}]` — month grid. Uniform plain cells carrying the level's emoji (😌 🙂 😟 😰 😱 via `AnxietyRules.Emoji`, `text-lg`) plus an emoji legend. **No shading.**

The grid is built on the generic `ReportRules.BuildWeeks<TCell>`; the med report is another wrapper over the same thing — see [reports.md](./reports.md).
