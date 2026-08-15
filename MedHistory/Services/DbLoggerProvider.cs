using System.Threading.Channels;
using MedHistory.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace MedHistory.Services;

/// <summary>
/// Writes log records to the Postgres "Logs" table.
///
/// Three things keep this from eating the app it is logging for:
/// <list type="bullet">
/// <item>It never touches <see cref="Data.AppDbContext"/>. Rows go out over a
/// dedicated <see cref="NpgsqlDataSource"/> with a parameterised INSERT, so
/// there is no scoped DbContext to capture and no EF command logging to feed
/// this provider its own writes.</item>
/// <item>Call sites never wait on the database. <see cref="DbLogger.Log"/>
/// drops a record into a bounded channel and returns; a single background task
/// does the inserts. A full queue discards the newest record rather than
/// blocking a request.</item>
/// <item>Its own failures never surface as exceptions or as log records —
/// logging a failed insert through <see cref="ILogger"/> would come straight
/// back here.</item>
/// </list>
///
/// The alias is what binds the "Logging:DbLogger" section to this provider.
/// </summary>
[ProviderAlias("DbLogger")]
public sealed class DbLoggerProvider : ILoggerProvider
{
    private const int QueueCapacity = 1000;

    private const string InsertSql = """
        INSERT INTO "Logs" ("Timestamp", "Level", "Category", "Message", "Exception", "RequestPath")
        VALUES (@timestamp, @level, @category, @message, @exception, @requestPath)
        """;

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly NpgsqlDataSource? _dataSource;
    private readonly Channel<PendingLog>? _queue;
    private readonly Task? _consumer;

    // Touched only by the single consumer task.
    private bool _reportedFailure;

    private bool _disposed;

    public DbLoggerProvider(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Design-time (dotnet ef) and tests run with no connection string.
            // Database logging switches itself off instead of failing the host.
            Console.Error.WriteLine("DbLogger: no connection string configured — database logging is off.");
            return;
        }

        try
        {
            // Deliberately built without a logger factory: Npgsql's own
            // diagnostics must not travel back through this provider.
            _dataSource = NpgsqlDataSource.Create(connectionString);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"DbLogger: database logging is off, could not build a data source — {ex.Message}");
            return;
        }

        _queue = Channel.CreateBounded<PendingLog>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>
    /// Supplies the request path column. Assigned from Program.cs once the
    /// container exists — the provider is constructed before it does.
    /// </summary>
    public IHttpContextAccessor? HttpContextAccessor { get; set; }

    internal bool IsActive => _queue is not null;

    public ILogger CreateLogger(string categoryName) => new DbLogger(this, categoryName);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _queue?.Writer.TryComplete();

        try
        {
            // Bounded: a wedged database must not hold up shutdown.
            _consumer?.Wait(DrainTimeout);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"DbLogger: shutdown drain failed — {ex.Message}");
        }

        _dataSource?.Dispose();
    }

    internal void Enqueue(in PendingLog record) => _queue?.Writer.TryWrite(record);

    internal string? CurrentRequestPath()
    {
        var path = HttpContextAccessor?.HttpContext?.Request.Path.Value;

        return DbLogFilter.Truncate(path, LogEntry.RequestPathMaxLength);
    }

    private async Task ConsumeAsync()
    {
        var reader = _queue!.Reader;

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var record))
            {
                await InsertAsync(record).ConfigureAwait(false);
            }
        }
    }

    private async Task InsertAsync(PendingLog record)
    {
        try
        {
            await using var command = _dataSource!.CreateCommand(InsertSql);

            // Types are stated rather than inferred so a null column value does
            // not depend on Npgsql guessing right from an untyped DBNull.
            Bind(command, "timestamp", NpgsqlDbType.TimestampTz, record.Timestamp);
            Bind(command, "level", NpgsqlDbType.Varchar, record.Level);
            Bind(command, "category", NpgsqlDbType.Varchar, record.Category);
            Bind(command, "message", NpgsqlDbType.Text, record.Message);
            Bind(command, "exception", NpgsqlDbType.Text, record.Exception);
            Bind(command, "requestPath", NpgsqlDbType.Varchar, record.RequestPath);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            _reportedFailure = false;
        }
        catch (Exception ex)
        {
            // Console only. Reporting through ILogger would queue another
            // record, and a down database would then feed itself.
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                Console.Error.WriteLine($"DbLogger: dropping log rows, insert failed — {ex.Message}");
            }
        }
    }

    private static void Bind(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
}

internal readonly record struct PendingLog(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception,
    string? RequestPath);

internal sealed class DbLogger : ILogger
{
    private readonly DbLoggerProvider _provider;
    private readonly string _category;

    internal DbLogger(DbLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        _provider.IsActive && DbLogFilter.ShouldWrite(_category, logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception) ?? string.Empty;

        if (message.Length == 0 && exception is null)
        {
            return;
        }

        _provider.Enqueue(new PendingLog(
            DateTimeOffset.UtcNow,
            logLevel.ToString(),
            DbLogFilter.Truncate(_category, LogEntry.CategoryMaxLength) ?? string.Empty,
            message,
            exception?.ToString(),
            _provider.CurrentRequestPath()));
    }
}
