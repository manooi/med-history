using MedHistory.Models;
using MedHistory.Services;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

/// <summary>
/// The database side of ticking and unticking a checklist slot — creating or removing the Med
/// <see cref="Entry"/> a tick control stands for. <see cref="MedHistory.Controllers.DayController"/>
/// resolves the slot and screens out a double submit before calling in; what a real tick or
/// untick does to the database lives here.
/// </summary>
public static class ChecklistStore
{
    /// <summary>
    /// Creates and saves the Med entry a tick on <paramref name="allocation"/>'s
    /// <paramref name="slot"/> stands for. The dose and stock link are stamped from the
    /// allocation as it stands right now — see Entry.DoseQuantity and Entry.MedStockId — so a
    /// later edit to the plan never rewrites an entry already ticked.
    /// </summary>
    public static async Task<Entry> TickAsync(this AppDbContext db, MedAllocation allocation, MedSlots slot)
    {
        var entry = new Entry
        {
            Type = BuiltInEntryTypes.Med,
            PillName = allocation.Name,
            OccurredAt = AppTime.TickTime(allocation.Day, slot),
            ChecklistAllocationId = allocation.Id,
            ChecklistSlot = MedPlanRules.SlotName(slot),
            DoseQuantity = allocation.DoseQuantity,
            MedStockId = allocation.MedStockId,
            // The timeline shows the note as typed, so the slot, the dose and how it is taken
            // are written into it — otherwise a ticked dose reads as a bare medication name.
            Note = MedPlanRules.ComposeNote(slot, allocation.MealRelation, allocation.Method, allocation.DoseQuantity)
        };

        db.Entries.Add(entry);
        await db.SaveChangesAsync();

        return entry;
    }

    /// <summary>
    /// Deletes the entry a tick created, if it is still there — a double-submit untick has
    /// already lost its entry and this quietly does nothing. Photos go with it — the FK is ON
    /// DELETE CASCADE. Returns the deleted entry, or null when there was nothing to delete.
    /// </summary>
    public static async Task<Entry?> UntickAsync(this AppDbContext db, int entryId)
    {
        var entry = await db.Entries.FindAsync(entryId);

        if (entry is not null)
        {
            db.Entries.Remove(entry);
            await db.SaveChangesAsync();
        }

        return entry;
    }
}
