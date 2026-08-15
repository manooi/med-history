namespace MedHistory.Models;

/// <summary>
/// One row in the application log table. Written only by
/// <see cref="Services.DbLoggerProvider"/>, which inserts rows with raw SQL
/// rather than through <see cref="Data.AppDbContext"/>.
/// </summary>
public class LogEntry
{
    // The provider clips to these before inserting, so the schema and the
    // writer have to agree on them.
    public const int LevelMaxLength = 16;
    public const int CategoryMaxLength = 256;
    public const int RequestPathMaxLength = 512;

    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }

    public string? RequestPath { get; set; }
}
