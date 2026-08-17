# Med stock

## `MedStocks`

`Name` is unique via a raw-SQL `lower(Name)` index — **outside the EF snapshot**, same as `EntryTypes`. Both `Name` and `TotalCount` are editable, one form per row.

The stock section reports through `StockErrors`, **not** ModelState — there are two forms on one page.

## Doses link to stock by id

`MedAllocation.MedStockId` and `Entry.MedStockId` — `int?`, no FK, dangling tolerated.

- Allocation ids are resolved by name on every write, and **re-resolved for all allocations** after any stock add / rename / remove (`MedStockRules.Relink`).
- A tick stamps the id onto the entry.

Net effect: **ticked doses survive stock renames** (they hold the id), while **manual doses follow the stock's current name** (they match by name).

## Consumption query

One grouped query over Med entries keyed by the `(MedStockId, PillName)` pair — `Data/StockQueries.StockRowsAsync`, shared by both pages.

Per stock, consumed = id-linked entries **plus** a `NamesMatch` fallback for id-`NULL` entries only (`DrawsOn`). Id-linked entries never double-count via name.

## The migration asymmetry

`AddStockLinks` backfilled ids for **all** pre-existing Med entries by name, freezing the then-visible counts. So: old manual doses are id-linked; manual doses created after that migration are name-only. Intentional.

## Remaining count

May go negative. It never blocks a tick.
