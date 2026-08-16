using System.Net;
using MedHistory.Services;

namespace MedHistory.Tests;

public class RateLimitRulesTests
{
    // ---- PartitionKey ----

    [Fact]
    public void PartitionKey_NullAddress_ReturnsUnknown()
    {
        Assert.Equal("unknown", RateLimitRules.PartitionKey(null));
    }

    [Fact]
    public void PartitionKey_PlainIPv4_ReturnsDottedString()
    {
        var address = IPAddress.Parse("203.0.113.7");

        Assert.Equal("203.0.113.7", RateLimitRules.PartitionKey(address));
    }

    [Fact]
    public void PartitionKey_IPv4MappedIPv6_NormalizesToSameKeyAsPlainIPv4()
    {
        var mapped = IPAddress.Parse("::ffff:203.0.113.7");
        var plain = IPAddress.Parse("203.0.113.7");

        Assert.Equal(RateLimitRules.PartitionKey(plain), RateLimitRules.PartitionKey(mapped));
        Assert.Equal("203.0.113.7", RateLimitRules.PartitionKey(mapped));
    }

    [Fact]
    public void PartitionKey_PlainIPv6_StaysIPv6String()
    {
        var address = IPAddress.Parse("2001:db8::1");

        Assert.Equal(address.ToString(), RateLimitRules.PartitionKey(address));
    }

    // ---- RetryAfterSeconds ----

    [Fact]
    public void RetryAfterSeconds_Null_ReturnsWindowSeconds()
    {
        Assert.Equal(RateLimitRules.WindowSeconds, RateLimitRules.RetryAfterSeconds(null));
    }

    [Fact]
    public void RetryAfterSeconds_SubSecond_RoundsUpToOne()
    {
        Assert.Equal(1, RateLimitRules.RetryAfterSeconds(TimeSpan.FromSeconds(0.4)));
    }

    [Fact]
    public void RetryAfterSeconds_OnePointTwoSeconds_RoundsUpToTwo()
    {
        Assert.Equal(2, RateLimitRules.RetryAfterSeconds(TimeSpan.FromSeconds(1.2)));
    }

    [Fact]
    public void RetryAfterSeconds_ExactFiveSeconds_StaysFive()
    {
        Assert.Equal(5, RateLimitRules.RetryAfterSeconds(TimeSpan.FromSeconds(5)));
    }

    // ---- Constants sanity ----

    [Fact]
    public void Constants_MatchContract()
    {
        Assert.Equal(10, RateLimitRules.PermitLimit);
        Assert.Equal(60, RateLimitRules.WindowSeconds);
        Assert.Equal("login", RateLimitRules.PolicyName);
    }
}
