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

## Ticking without a reload

The slot controls are ordinary `<form method="post">` posts. A script in `Day/Index.cshtml` intercepts them and refetches instead.

- Client sends `X-Requested-With: XMLHttpRequest`. Server-side `LiveUpdateRules.IsFragmentRequest` (pure, tested) is the only thing that branches on it.
- Every return point in `Tick`/`Untick` — the double-submit early returns included — goes through `DayController.ChecklistResultAsync`, which renders `_DayLive` for a fragment request and the same old redirect otherwise. `NotFound()` is left alone.
- `_DayLive` is `#checklist-region` + `#entries-region`, the same two partials `Index` uses. The model is **re-read** so progress, stock label and the timeline cannot disagree. The timeline is in scope because a tick *is* an entry.
- Client swaps both regions' `innerHTML`, restores the `<details>` open state and refocuses the button by `data-slot="{allocationId}:{slot}"`.

> **Invariant:** anything unexpected — non-OK response, missing region, parse throw — calls `form.submit()` and lets the browser do the old full post. The reader never sees state the server didn't send. That fallback is also the no-JS path, so **never remove `[ValidateAntiForgeryToken]`** or change the form markup's POST contract.

One page-wide in-flight flag, not per-form: two slots posting against the same render would let arrival order decide the day.

Anything that binds behaviour to entries or checklist markup must use **document-level delegation** (as `_Lightbox` does) — per-element listeners die on the swap.
