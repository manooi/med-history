# Epic: Personal medical history web app (`med-history-4ei`)

## Why

Single-user personal medical tracker. Log daily health events as multiple timestamped entries (symptom, bleeding, pill taken, cough, meal), attach photos, browse history. Protects medical data behind a single password.

## Stack

- ASP.NET Core MVC on .NET 10, project `MedHistory/` at repo root
- PostgreSQL via EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL`, code-first migrations
- Photos stored in DB as `bytea` (user decision; base64 rejected for +33% size)
- Tailwind CSS v4 via npm, strict black/white/neutral theme — no color anywhere
- Cookie auth, one password from user-secrets `Auth:Password`
- Connection string from user-secrets `ConnectionStrings:Default` — **not yet provided**; migrations apply + runtime verification blocked on it, code work is not

## Data model

```
Entry:  Id, OccurredAt timestamptz, Type enum(Symptom|Bleeding|Pill|Cough|Meal),
        Note text?, Severity enum?(Light|Moderate|Severe — Bleeding/Cough only),
        PillName text? (Pill only)          — enums stored as strings
Photo:  Id, EntryId FK cascade, Data bytea, ContentType, FileName, CreatedAt
```

## Scope

| bd id | Item | Model | Depends on |
|---|---|---|---|
| `4ei.1` | Scaffold dotnet MVC | sonnet | — |
| `4ei.2` | Tailwind + layout shell | sonnet | .1 |
| `4ei.3` | EF Core data layer | opus | .1 |
| `4ei.4` | Cookie auth | sonnet | .1 |
| `4ei.5` | Entry CRUD + day view | opus | .2 .3 .4 |
| `4ei.6` | Photo upload/serving | sonnet | .5 |
| `4ei.7` | History view | sonnet | .5 |
| `4ei.8` | xUnit pure-logic tests | sonnet | .5 |

## Build order

1. Batch 1 (serial): `.1` scaffold — everything branches off it
2. Batch 2 (parallel ×3, separate worktrees): `.2` `.3` `.4` — merges serial ff-only; known `Program.cs` overlap between .3/.4 resolved at merge
3. Batch 3 (serial): `.5` core feature
4. Batch 4 (parallel ×3): `.6` `.7` `.8`

## Checklist

- [x] 4ei.1 scaffold
- [x] 4ei.2 tailwind
- [x] 4ei.3 data layer
- [x] 4ei.4 auth
- [x] 4ei.5 entry CRUD + day view
- [ ] 4ei.6 photos
- [x] 4ei.7 history
- [x] 4ei.8 tests
