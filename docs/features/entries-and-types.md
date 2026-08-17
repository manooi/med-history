# Entries, entry types, photos

## Entries

Multiple entries per day, timestamped `OccurredAt` (`timestamptz`). The day view groups by **local** date.

`Entry.Type` is a plain string — **no FK**. Validation is app-level, in `EntryTypeRules`.

## Entry types (data-driven)

Rows in the `EntryTypes` table, managed at `/types`.

**Six seeded built-ins:** Symptom, Bleeding, Med, Cough, Meal, Note.

- Med was called "Pill" until the `RenamePillTypeToMed` data migration — **the C# identifiers still say Pill**.
- Note was seeded by `AddNoteEntryType` behind a `lower(Name)` guard; a pre-existing *custom* `note` stays custom via an ordinal match.

Built-ins keep special fields; user-added types do not:

| Field | Applies to |
|---|---|
| `Severity` | Bleeding, Cough |
| `PillName` | Med |
| note text (required) | Symptom, Note |

User-added types are name-only: note + photos + time.

**Deactivate** hides a type from the new-entry UI. It never deletes — history stays viewable.

> **Invariant:** type-name uniqueness is a raw-SQL `lower(Name)` index that lives **outside the EF snapshot**. Later migrations will not see it. (Same pattern as `MedStocks` — see [med-stock.md](./med-stock.md).)

## Photos

Stored as `bytea` in the DB, served from `/photos/{id}`. Cap 10 MB per photo, `image/*` only.

Client JS downscales anything over 1600 px to 1600 px JPEG q0.85 before upload (in `Form.cshtml`). A decode failure — HEIC, typically — falls back to the original file. A generation counter guards against re-selection races.
