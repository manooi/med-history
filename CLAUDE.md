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

- Theme: strict black/white/neutral grays only — **no color Tailwind classes ever**; severity via label text + border weight
- Enums stored as strings in Postgres
- Secrets only in user-secrets: `ConnectionStrings:Default`, `Auth:Password` — never in appsettings
- Photos: bytea in DB, served via `/photos/{id}`, 10 MB/photo cap, image/* only
- Decision logic extracted as pure functions (testability rule)

## Business rules

- Entry types are data-driven (`EntryTypes` table, managed at `/types`): 5 seeded built-ins (Symptom, Bleeding, Pill, Cough, Meal) keep special fields (`Severity` only Bleeding/Cough; `PillName` only Pill); user-added types are name-only (note+photos+time). Deactivate hides a type from new-entry UI, never deletes. `Entry.Type` is a plain string (no FK — app-level validation in `EntryTypeRules`); type-name uniqueness via raw-SQL `lower(Name)` index that lives OUTSIDE the EF snapshot — later migrations won't see it
- Multiple entries per day, timestamped `OccurredAt` (timestamptz), day view groups by local date

## Problems log

`problems/PROBLEMS.md` (one-line index) + `problems/PROBLEMS_DETAILS.md` (full write-ups). Every resolved bug → both files + regression test same commit.

## Roadmap / plans

`ROADMAP.md` numbered items — tick `[x]` in the same commit as the implementation. Epic plan: `plans/epic-medhistory.md`.

## Beads

Epic `med-history-4ei`, children `.1`–`.8`. `bd ready` / `bd show <id>` / `bd update <id> --claim` / `bd close <id>`. `.beads/issues.jsonl` is passive export — never hand-merge.
