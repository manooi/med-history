using MedHistory.Services;

namespace MedHistory.Tests;

public class LiveUpdateRulesTests
{
    [Fact]
    public void IsFragmentRequest_ExactHeaderValue_IsTrue()
    {
        Assert.True(LiveUpdateRules.IsFragmentRequest(LiveUpdateRules.FragmentHeaderValue));
    }

    [Theory]
    [InlineData("xmlhttprequest")]
    [InlineData("XMLHTTPREQUEST")]
    [InlineData("XmlHttpRequest")]
    public void IsFragmentRequest_AnyCasing_IsTrue(string header)
    {
        Assert.True(LiveUpdateRules.IsFragmentRequest(header));
    }

    [Fact]
    public void IsFragmentRequest_MissingHeader_IsFalse()
    {
        Assert.False(LiveUpdateRules.IsFragmentRequest(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void IsFragmentRequest_EmptyHeader_IsFalse(string header)
    {
        Assert.False(LiveUpdateRules.IsFragmentRequest(header));
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("XMLHttpRequest2")]
    [InlineData(" XMLHttpRequest")]
    [InlineData("XMLHttpRequest ")]
    public void IsFragmentRequest_OtherValue_IsFalse(string header)
    {
        Assert.False(LiveUpdateRules.IsFragmentRequest(header));
    }
}
