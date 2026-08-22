# CLAUDE.md

## Project

Personal medical history web app — single user. ASP.NET Core MVC (.NET 10) + PostgreSQL (EF Core/Npgsql) + Tailwind v4. Daily timestamped entries, photos stored in the DB as `bytea`, single-password cookie auth.

## Repo layout

- `MedHistory/` — app source; `cd MedHistory && dotnet build|run|watch`, css: `pnpm run css`
- `MedHistory.Tests/` — xUnit; `dotnet test` from repo root
- `docs/features/` — per-subsystem deep dives (read before touching that subsystem)
- Repo root — `plans/`, `problems/`, `ROADMAP.md`, this file, `.beads/`, `.claude/`

## Working process

Global playbook applies (`~/CLAUDE.md` gate):

- **No bead, no code.**
- Orchestrator never edits `MedHistory*/` source — all src goes through the `builder` agent (`.claude/agents/builder.md`; opus for design-heavy beads, sonnet for bounded ones).
- One bead = one worktree `../med-history-wt/<id>` = one branch `bead/<id>`. Merges are serial, `--ff-only`.

## Conventions

- **Theme:** black/white/neutral only, red reserved for destructive actions → [theming.md](./docs/features/theming.md)
- **Enums** are stored as strings in Postgres
- **Secrets** only in user-secrets (`ConnectionStrings:Default`, `Auth:Password`) — never in `appsettings`
- **Decision logic is extracted as pure functions** and unit-tested. If logic is buried in a controller or view, pull it into a `Services/*Rules.cs` and test that.

## Feature docs

| Doc | Covers |
|---|---|
| [theming.md](./docs/features/theming.md) | palette, red carve-out, dark mode + palette map, print |
| [auth.md](./docs/features/auth.md) | 6-digit passcode screen, login throttling, secrets |
| [entries-and-types.md](./docs/features/entries-and-types.md) | entries, data-driven entry types, photos |
| [med-checklist.md](./docs/features/med-checklist.md) | allocations, slots, ticks, retro-tick times |
| [med-stock.md](./docs/features/med-stock.md) | stock rows, id-vs-name dose linking, consumption query |
| [anxiety.md](./docs/features/anxiety.md) | day vote (toggle-to-clear), month emoji grid |
| [reports.md](./docs/features/reports.md) | hub, type / med / weight / doctor reports, search |
| [navigation.md](./docs/features/navigation.md) | desktop + mobile nav, theme toggle wiring, PWA |
| [i18n.md](./docs/features/i18n.md) | en-US / th-TH, culture cookie + toggle, Buddhist-era dates |

## Invariants

The expensive-to-learn rules. Don't rederive them — full context in the linked doc.

- Every new light class in a view carries its `dark:` counterpart **on the same line**; dark utilities are inert under print → [theming](./docs/features/theming.md)
- The theme toggle exists **twice** — bind by class `.theme-toggle`, never by id → [navigation](./docs/features/navigation.md)
- The language toggle exists **three times** (desktop nav, mobile panel, logged-out bar) and is a plain form POST, so it works with JS off → [i18n](./docs/features/i18n.md)
- Day keys, month/datetime-local input values and posted decimals are formatted with an explicit `InvariantCulture`; under `th-TH` an implicit format yields `2569-…` and 404s the route → [i18n](./docs/features/i18n.md)
- Culture comes from the cookie **only** — `Accept-Language` is deliberately not a provider, so a Thai browser can't silently flip the printed doctor report → [i18n](./docs/features/i18n.md)
- A control that pins its height to the font size (`leading-none`, an inline border with no `py-*`) crops Thai tone marks — it carries a `th:` counterpart **on the same line**, the way `dark:` does → [i18n](./docs/features/i18n.md)
- No view may call native `confirm()` (a test enforces it) — destructive buttons carry `data-confirm` and the shared `_ConfirmDialog` re-submits via `requestSubmit` → [theming](./docs/features/theming.md)
- `Auth:Password` must be **exactly 6 digits** → [auth](./docs/features/auth.md)
- A locked login POST neither checks the password nor records an attempt (expiry can't be pushed) → [auth](./docs/features/auth.md)
- `lower(Name)` unique indexes (`EntryTypes`, `MedStocks`) are raw SQL **outside the EF snapshot** — later migrations won't see them → [entries-and-types](./docs/features/entries-and-types.md), [med-stock](./docs/features/med-stock.md)
- `Entry.Type` is a plain string, **no FK** — validation is app-level in `EntryTypeRules` → [entries-and-types](./docs/features/entries-and-types.md)
- The canvas downscale **strips EXIF**, so a photo's capture date must be read in the browser from the original `File` before the swap — server-side parsing is not an option → [entries-and-types](./docs/features/entries-and-types.md)
- Dose links (`ChecklistAllocationId`, `MedStockId`) are nullable with **no FK**; dangling ids are tolerated everywhere → [med-checklist](./docs/features/med-checklist.md)
- A tick **stamps** the allocation's `DoseQuantity` + `MedStockId` onto the entry; plan edits never rewrite logged entries → [med-checklist](./docs/features/med-checklist.md)
- Tick/untick posts are intercepted by fetch and swap two regions; **any** failure falls back to the real form post, so the plain POST contract and antiforgery must stay intact. Behaviour bound to day-page markup needs document-level delegation → [med-checklist](./docs/features/med-checklist.md)
- Manual Med entries do **not** count toward slots, but **do** consume stock → [med-stock](./docs/features/med-stock.md)
- `DoseQuantity` is posted as a raw string and parsed invariant-culture — never model-bound as `decimal` → [med-checklist](./docs/features/med-checklist.md)
- The doctor report is the **only** report that reads oldest-first → [reports](./docs/features/reports.md)
- Med report counts on a **plan-day basis**, so it can disagree with the day page for a hand-edited entry → [reports](./docs/features/reports.md)
- A rule's user-facing sentence returns a **key plus numbered args** (`RuleMessage`), never interpolated copy — once the holes are filled there is no key left to translate, and only the view can format them into the *Thai* word order → [i18n](./docs/features/i18n.md)
- A view that splits a composed string splits on `MedPlanRules.PartSeparator`, **never** a copy of `" · "` — a second literal drifts silently and the description renders as one unlooked-up English blob → [i18n](./docs/features/i18n.md)

## Problems log

`problems/PROBLEMS.md` (one-line index) + `problems/PROBLEMS_DETAILS.md` (full write-ups). Every resolved bug → **both** files + a regression test, same commit.

## Roadmap / plans

`ROADMAP.md` numbered items — tick `[x]` in the same commit as the implementation. Epic plan: `plans/epic-medhistory.md`.

## Beads

Epic `med-history-4ei`. `bd ready` / `bd show <id>` / `bd update <id> --claim` / `bd close <id>`. `.beads/issues.jsonl` is a passive export — never hand-merge.
