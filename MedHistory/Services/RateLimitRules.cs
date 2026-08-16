using System.Net;

namespace MedHistory.Services;

/// <summary>
/// Pure per-IP rate-limit rules for <c>/login</c> — no ASP.NET types beyond
/// <see cref="IPAddress"/>, so the partition key and retry-after math stay unit-testable without
/// spinning up the middleware.
/// </summary>
public static class RateLimitRules
{
    /// <summary>Name of the named rate-limiter policy applied to the login GET/POST actions.</summary>
    public const string PolicyName = "login";

    /// <summary>Width of the fixed window the limiter counts requests within.</summary>
    public const int WindowSeconds = 60;

    /// <summary>Requests permitted per partition per <see cref="WindowSeconds"/> window.</summary>
    public const int PermitLimit = 10;

    /// <summary>
    /// The partition key a caller is grouped under. IPv4-mapped IPv6 addresses (the form a
    /// dual-stack socket reports for an IPv4 peer, e.g. <c>::ffff:203.0.113.7</c>) are normalized
    /// to their IPv4 form so the same client isn't split across two partitions depending on which
    /// address family accepted the connection. A missing address (no connection info available)
    /// falls into a single shared "unknown" partition rather than being unlimited.
    /// </summary>
    public static string PartitionKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }

    /// <summary>
    /// Seconds to report in the <c>Retry-After</c> header for a rejected request. A missing lease
    /// hint (the limiter didn't supply one) falls back to the full <see cref="WindowSeconds"/>;
    /// otherwise the value is rounded up to whole seconds with a floor of 1, since 0 would tell the
    /// caller to retry immediately.
    /// </summary>
    public static int RetryAfterSeconds(TimeSpan? retryAfter)
    {
        if (retryAfter is null)
        {
            return WindowSeconds;
        }

        return Math.Max(1, (int)Math.Ceiling(retryAfter.Value.TotalSeconds));
    }
}
