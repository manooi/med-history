# Med checklist (allocations, slots, ticks)

Vocabulary lives in `MedPlanRules`, behaviour in `ChecklistRules`.

## The plan: `MedAllocations`

Per-day rows. Columns worth knowing:

- `Name`
- `Slots` — `[Flags]` Morning / Noon / Evening / Bedtime, stored as canonical CSV via an explicit value converter
- `MealRelation`, `Method` — enums, stored as strings
- `DoseQuantity` — `numeric(5,2)`, default 1, 0.25 steps. Posted as a **raw string** and parsed invariant-culture — never model-bound as `decimal`.

Maintenance lives at `/day/{date}/meds` (`MedsController`), including range-add and edit-with-apply-forward. Tick/untick lives on `DayController`.

## Ticks are per slot

A tick creates a real Med `Entry`, linked by `Entry.ChecklistAllocationId` + `Entry.ChecklistSlot`.

> **Invariant:** those link columns are nullable with **no FK** — a logged dose outlives the plan that produced it. Dangling allocation ids are tolerated everywhere.

At tick time the entry is **stamped** with the allocation's current `DoseQuantity` and `MedStockId`. Later plan edits never rewrite already-logged entries. `DoseQuantity == null` means one unit — that rule lives in `MedStockRules.UsageQuantity`.

Hand-editing an entry's `PillName` clears its `MedStockId` (the link is now stale) unless the name is `NamesMatch`-unchanged.

### Timestamps

- Tick on **today** → stamps now.
- Retro tick on a **past day** → stamps the slot's canonical local time: morning 09:00, noon 12:00, evening 18:00, bedtime 22:00 (`MedPlanRules.SlotTime`, noon as fallback).

### Untick

Deletes exactly the linked entry, scoped to the day.

### Slot state

Slot is ticked **iff** a linked entry exists. Manual (hand-typed) Med entries do **not** count toward slots — but they **do** consume stock. See [med-stock.md](./med-stock.md).
