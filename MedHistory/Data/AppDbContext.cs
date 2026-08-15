using MedHistory.Models;
using Microsoft.EntityFrameworkCore;

namespace MedHistory.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Entry> Entries => Set<Entry>();

    public DbSet<Photo> Photos => Set<Photo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Entry>(entry =>
        {
            entry.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entry.Property(e => e.Severity)
                .HasConversion<string>()
                .HasMaxLength(32);

            entry.HasIndex(e => e.OccurredAt);
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
    }
}
