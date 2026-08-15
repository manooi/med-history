# Epic: controller refactor — split Meds + Data query layer (`med-history-9b1`)

## Why

Controllers accreted EF orchestration and viewmodel assembly while the decision logic moved
to pure `Services/*Rules`. The pain is concentrated, not general: `MedsController` is 648
lines holding two resources (allocations + stock) with five private EF helpers; `EntriesController`
carries the photo pipeline inline; `DayController.ShowDay` is an 84-line assembly. User verdict:
"you write everything in controller" → chosen scope: **split by resource + extract a Data/ query
layer** (house precedent: `Data/StockQueries`). No route changes, no behavior changes, tests stay
green throughout. Explicitly NOT chosen: full handler/CQRS layer (too much ceremony for a
single-user app).

## Scope

| Bead | What | Depends on |
|---|---|---|
| `9b1.1` | Split `MedsController` → allocations stay, stock CRUD → new `StocksController` (pure move, routes verbatim) | — |
| `9b1.2` | `Data/AllocationQueries` — dedupe allocation/tick/slot EF helpers across Meds/Stocks/Day controllers | 9b1.1 |
| `9b1.3` | `EntriesController` slim — photo pipeline → `Data/PhotoStore`; `CopyInto`/`ApplyRules` → `EntryRules` (+ tests for moved pure logic, MedStockId-clearing rule preserved) | — |
| `9b1.4` | `DayController` slim — `ShowDay` assembly → `Data/DayQueries.DayPageAsync` | — |

## Build order

1. `9b1.1` + `9b1.3` in parallel (disjoint files) — `9b1.4` follows in the next slot.
2. `9b1.2` after `9b1.1` lands (extracts from the post-split controllers; coordinates with
   `9b1.4` on `Ticks`/`ResolveSlot` — whoever lands second rebases).

## Invariants (frozen)

- Every URL answers identically — attribute routes copied verbatim.
- No query shape changes: same SQL, same `AsNoTracking`, same ordering.
- `CopyInto`'s MedStockId-clearing rule (CLAUDE.md business rule) moves untouched, gains tests.
- 675-test baseline green after every bead; each bead lands separately via `--ff-only`.

## Checklist

- [x] 9b1.1 split MedsController / StocksController
- [ ] 9b1.2 Data/AllocationQueries
- [x] 9b1.3 EntriesController slim (PhotoStore + EntryRules moves)
- [ ] 9b1.4 DayController slim (DayQueries)
