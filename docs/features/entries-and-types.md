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

### "Use photo date"

A button in the time-preset row (next to 09:00 / 12:00 / 18:00) that sets the entry time to the photo's EXIF capture time.

> **Invariant:** the downscale re-encodes through a canvas, which **strips EXIF**. The server never sees it, so there is nothing to parse server-side — the date is read in the browser, from the *original* `File` objects, before the swap. The whole reader is inline in `Form.cshtml` next to the downscale code; there is no JS test runner and no npm dependency for it.

- Reads only the first 256 KB of each file (`file.slice`) — the APP1 segment sits near the start and is capped at 64 KB.
- Tag priority: ExifIFD `0x9003` DateTimeOriginal → ExifIFD `0x9004` DateTimeDigitized → IFD0 `0x0132` DateTime. Both `II` and `MM` byte orders.
- Multiple photos: the **first file in selection order** that yields a date wins.
- **No date found → the button stays hidden.** No disabled state and no fallback to `file.lastModified` — that is usually the download/AirDrop time, i.e. wrong data in a medical log. A fresh selection that yields nothing hides it again.
- Blank / all-zero / out-of-range values (month, day, hour, minute) count as absent. Every buffer read is bounds-checked and the parser is wrapped so a corrupt or truncated image resolves to `null` and never blocks the upload.
- The parsed value goes in the button's `title`, not its label — a long label wraps the row on mobile. Clicking writes `YYYY-MM-DDTHH:mm` straight into `#OccurredAt`; EXIF is already local wall-clock at capture, so there is no timezone conversion.
- Shares the existing `photosGeneration` counter, so a fast re-selection can't let a stale result win. The counter is bumped **before** the handler's empty-selection early return — clearing the picker must invalidate work in flight for the downscale swap as well as the EXIF read (problem 6).
