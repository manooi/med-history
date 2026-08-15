# CLAUDE.md

## Project

Personal medical history web app — single user. ASP.NET Core MVC (.NET 10) + PostgreSQL (EF Core/Npgsql) + Tailwind v4. Daily timestamped entries (symptom, bleeding, pill, cough, meal), photos stored in DB as bytea, single-password cookie auth.

## Repo layout

- `MedHistory/` — app source; dev commands: `cd MedHistory && dotnet build|run|watch`, css: `npm run css`
- `MedHistory.Tests/` — xUnit tests; `dotnet test` from repo root
- Repo root — `plans/`, `problems/`, `ROADMAP.md`, this file, `.beads/`, `.claude/`

## Working process

Global playbook applies (~/CLAUDE.md gate): **no bead no code**, orchestrator never edits `MedHistory*/` source — all src via `builder` agent (`.claude/agents/builder.md`, model per bead: opus = design-heavy, sonnet = bounded). One bead = one worktree `../med-history-wt/<id>` = one branch `bead/<id>`; merges serial `--ff-only`.

## Conventions

- Theme: black/white/neutral grays — **one carve-out: destructive actions (delete/remove buttons) use red** (`text-red-600`, `hover:text-red-800`, bordered: `border-red-600 hover:bg-red-600 hover:text-white`); deactivate is NOT destructive (stays neutral). No other color classes ever; severity via label text + border weight
- Enums stored as strings in Postgres
- Secrets only in user-secrets: `ConnectionStrings:Default`, `Auth:Password` — never in appsettings
- Photos: bytea in DB, served via `/photos/{id}`, 10 MB/photo cap, image/* only
- Decision logic extracted as pure functions (testability rule)

## Business rules

- Entry types are data-driven (`EntryTypes` table, managed at `/types`): 6 seeded built-ins (Symptom, Bleeding, Med, Cough, Meal, Note — Med was "Pill" until the RenamePillTypeToMed data migration, C# identifiers still say Pill; Note seeded by AddNoteEntryType with lower(Name) guard, a pre-existing custom 'note' stays custom via ordinal match) keep special fields (`Severity` only Bleeding/Cough; `PillName` only Med; note text required only Symptom/Note); user-added types are name-only (note+photos+time). Deactivate hides a type from new-entry UI, never deletes. `Entry.Type` is a plain string (no FK — app-level validation in `EntryTypeRules`); type-name uniqueness via raw-SQL `lower(Name)` index that lives OUTSIDE the EF snapshot — later migrations won't see it
- Multiple entries per day, timestamped `OccurredAt` (timestamptz), day view groups by local date
- Med checklist: `MedAllocations` per-day rows — Name + `Slots` ([Flags] Morning/Noon/Evening/Bedtime, stored canonical CSV via explicit converter) + MealRelation + Method (enum-as-string) + `DoseQuantity` (numeric(5,2), default 1, 0.25 steps, posted as raw string + invariant-culture parsed — never decimal-bound). Ticks are PER SLOT: tick creates a Med entry linked via `Entry.ChecklistAllocationId`+`ChecklistSlot` (nullable, NO FK — a logged dose outlives its plan) and STAMPS the allocation's current DoseQuantity onto `Entry.DoseQuantity` (null = one unit, rule lives in `MedStockRules.UsageQuantity`) plus the allocation's `MedStockId` onto `Entry.MedStockId`; plan edits never rewrite logged entries. Hand-editing an entry's PillName clears its `MedStockId` (stale link) unless the name is NamesMatch-unchanged. Retro ticks (past day) stamp the slot's canonical local time — morning 09:00 / noon 12:00 / evening 18:00 / bedtime 22:00 (`MedPlanRules.SlotTime`, noon fallback); today's tick stamps now. Untick deletes exactly the linked entry (day-scoped). Slot state = linked entry exists; manual Med entries do NOT count toward slots but DO consume stock. Dangling allocation ids tolerated everywhere. Maintenance at `/day/{date}/meds` (`MedsController`, incl. range add + edit w/ apply-forward); tick/untick on `DayController`. Vocabulary in `MedPlanRules`, behavior in `ChecklistRules`
- Med stock: `MedStocks` (Name unique via raw-SQL `lower(Name)` index — outside EF snapshot like EntryTypes; Name AND TotalCount editable, one form per row). Doses link to stock BY ID: `MedAllocation.MedStockId` + `Entry.MedStockId` (int?, no FK, dangling tolerated) — allocation ids resolved by name on every write and re-resolved for ALL allocations after any stock add/rename/remove (`MedStockRules.Relink`); tick stamps the id. Consumed = ONE grouped query over Med entries by (MedStockId, PillName) pair (`Data/StockQueries.StockRowsAsync`, shared by both pages); per stock = id-linked entries + NamesMatch fallback for id-NULL entries only (`DrawsOn` — id-linked NEVER double-counts via name). So ticked doses survive stock renames; manual doses follow the stock's current name. `AddStockLinks` migration backfilled ids for ALL pre-existing Med entries by name (froze then-visible counts — old manual doses are id-linked, post-migration manual doses are name-only; intentional asymmetry). Remaining may go negative, never blocks ticks. Stock section uses `StockErrors`, not ModelState (two forms on one page)
- Anxiety vote: one editable per day (`AnxietyVotes`, unique Day index, `AnxietyLevel` Calm/Ok/Tense/Anxious/Panic enum-as-string). Day-page card under meds checklist; POST `/day/{date}/anxiety/{level}` toggles — same level again clears (pure `AnxietyRules.DecideVote`); racing first-vote double-submit swallowed via DbUpdateException guard (AddStock pattern). `/anxiety-report[/{yyyy-MM}]` month grid, cells shaded darker=worse with 1–5 legend; grid built on generic `ReportRules.BuildWeeks<TCell>` (med report is a wrapper over it)
- Med report: `/med-report[/{yyyy-MM}]` month calendar (`ReportController` + pure `ReportRules`, Monday-first grid), all meds combined — per day ticked/planned slot counts bucketed by allocation.Day (plan-day basis: link alone counts, entry hand-edited onto another date still counts here though the day page shows the slot unticked — documented on ReportRules). States: Full (ticked>=planned, solid black) / Partial / None / NoPlan. Slot semantics reuse ChecklistRules.FindTick + MedPlanRules — duplicate ticks collapse, dangling links ignored

## Problems log

`problems/PROBLEMS.md` (one-line index) + `problems/PROBLEMS_DETAILS.md` (full write-ups). Every resolved bug → both files + regression test same commit.

## Roadmap / plans

`ROADMAP.md` numbered items — tick `[x]` in the same commit as the implementation. Epic plan: `plans/epic-medhistory.md`.

## Beads

Epic `med-history-4ei`, children `.1`–`.8`. `bd ready` / `bd show <id>` / `bd update <id> --claim` / `bd close <id>`. `.beads/issues.jsonl` is passive export — never hand-merge.
