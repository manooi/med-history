using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Entry> Entries => Set<Entry>();

    // The types an entry can be created as. Seeded with the five built-ins; rows are
    // added from the /types page, which is what makes new entry types a data change
    // rather than a code change.
    public DbSet<EntryTypeDef> EntryTypes => Set<EntryTypeDef>();

    // Per-day medication checklist. Rows carry no link to Entries; the link runs the other
    // way, from Entry.ChecklistAllocationId, which is what a ticked slot is — see
    // ChecklistRules.
    public DbSet<MedAllocation> MedAllocations => Set<MedAllocation>();

    public DbSet<Photo> Photos => Set<Photo>();

    // Mapped so migrations own the schema; rows are inserted by DbLoggerProvider
    // over a separate connection, never through this context.
    public DbSet<LogEntry> Logs => Set<LogEntry>();

    // Data Protection keys live in Postgres so Cloud Run's ephemeral instances
    // stop invalidating login cookies and antiforgery tokens on every deploy.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Entry>(entry =>
        {
            // Plain text, not a foreign key — see the comment on Entry.Type. The column
            // is unchanged from when this was an enum: enums were already persisted by name.
            entry.Property(e => e.Type)
                .HasMaxLength(EntryTypeDef.NameMaxLength)
                .IsRequired();

            entry.Property(e => e.Severity)
                .HasConversion<string>()
                .HasMaxLength(32);

            // Which checklist slot this entry ticked, if any. ChecklistAllocationId is a plain
            // integer with no foreign key — see the comment on Entry.ChecklistAllocationId.
            entry.Property(e => e.ChecklistSlot)
                .HasMaxLength(MedPlanRules.SlotNameMaxLength);

            entry.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<EntryTypeDef>(type =>
        {
            type.ToTable("EntryTypes");

            type.Property(t => t.Name).HasMaxLength(EntryTypeDef.NameMaxLength).IsRequired();
            type.Property(t => t.IsActive).HasDefaultValue(true);

            // Names are unique case-insensitively, enforced by a unique index on
            // lower("Name") created in the AddEntryTypes migration: EF's model builder
            // has no API for an expression index, so it cannot live here.
        });

        modelBuilder.Entity<MedAllocation>(allocation =>
        {
            allocation.ToTable("MedAllocations");

            allocation.Property(a => a.Name).HasMaxLength(MedAllocation.NameMaxLength).IsRequired();

            // Slots are a set, stored as the canonical name list MedPlanRules defines rather
            // than as the flags integer: the column stays readable in psql and consistent with
            // the enums-as-strings the rest of the schema uses. Nothing queries by slot in SQL —
            // a day's allocations are always loaded whole — so there is no index to lose.
            allocation.Property(a => a.Slots)
                .HasConversion(
                    slots => MedPlanRules.FormatSlots(slots),
                    stored => MedPlanRules.ParseSlots(stored))
                .HasMaxLength(MedPlanRules.SlotsMaxLength)
                .IsRequired();

            allocation.Property(a => a.MealRelation)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            allocation.Property(a => a.Method)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            // Every read is "the allocations of one day".
            allocation.HasIndex(a => a.Day);
        });

        modelBuilder.Entity<Photo>(photo =>
        {
            photo.Property(p => p.Data).IsRequired();
            photo.Property(p => p.ContentType).HasMaxLength(128).IsRequired();
            photo.Property(p => p.FileName).HasMaxLength(260).IsRequired();

            photo.HasOne(p => p.Entry)
                .WithMany(e => e.Photos)
                .HasForeignKey(p => p.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LogEntry>(log =>
        {
            log.ToTable("Logs");

            log.Property(l => l.Level).HasMaxLength(LogEntry.LevelMaxLength).IsRequired();
            log.Property(l => l.Category).HasMaxLength(LogEntry.CategoryMaxLength).IsRequired();
            log.Property(l => l.Message).IsRequired();
            log.Property(l => l.RequestPath).HasMaxLength(LogEntry.RequestPathMaxLength);

            log.HasIndex(l => l.Timestamp);
        });
    }
}
