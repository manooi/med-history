using MedHistory.Services;

namespace MedHistory.Tests;

public class SearchRulesTests
{
    // ---- NormalizeQuery ----

    [Fact]
    public void NormalizeQuery_Null_IsNull()
    {
        Assert.Null(SearchRules.NormalizeQuery(null));
    }

    [Fact]
    public void NormalizeQuery_Empty_IsNull()
    {
        Assert.Null(SearchRules.NormalizeQuery(""));
    }

    [Fact]
    public void NormalizeQuery_AllWhitespace_IsNull()
    {
        Assert.Null(SearchRules.NormalizeQuery("   "));
        Assert.Null(SearchRules.NormalizeQuery("\t\n"));
    }

    [Fact]
    public void NormalizeQuery_TrimsSurroundingWhitespace()
    {
        Assert.Equal("headache", SearchRules.NormalizeQuery("  headache  "));
    }

    [Fact]
    public void NormalizeQuery_InnerWhitespace_IsPreserved()
    {
        Assert.Equal("severe headache", SearchRules.NormalizeQuery("  severe headache  "));
    }

    [Fact]
    public void NormalizeQuery_NoWhitespace_IsUnchanged()
    {
        Assert.Equal("ibuprofen", SearchRules.NormalizeQuery("ibuprofen"));
    }

    // ---- EscapeLike ----

    [Fact]
    public void EscapeLike_CleanText_IsUnchanged()
    {
        Assert.Equal("ibuprofen", SearchRules.EscapeLike("ibuprofen"));
    }

    [Fact]
    public void EscapeLike_Percent_IsEscaped()
    {
        Assert.Equal("50\\% dose", SearchRules.EscapeLike("50% dose"));
    }

    [Fact]
    public void EscapeLike_Underscore_IsEscaped()
    {
        Assert.Equal("pill\\_name", SearchRules.EscapeLike("pill_name"));
    }

    [Fact]
    public void EscapeLike_Backslash_IsEscaped()
    {
        Assert.Equal("a\\\\b", SearchRules.EscapeLike(@"a\b"));
    }

    [Fact]
    public void EscapeLike_Mixed_EscapesEveryOccurrence()
    {
        // Backslash first, so the wildcard escapes it introduces are not themselves re-escaped.
        Assert.Equal("50\\%\\_off\\\\sale", SearchRules.EscapeLike(@"50%_off\sale"));
    }

    [Fact]
    public void EscapeLike_MultipleOfSameChar_EscapesEachOne()
    {
        Assert.Equal("\\%\\%\\%", SearchRules.EscapeLike("%%%"));
    }

    [Fact]
    public void EscapeLike_Empty_IsEmpty()
    {
        Assert.Equal("", SearchRules.EscapeLike(""));
    }

    [Fact]
    public void EscapeLike_BackslashFollowedByWildcard_DoesNotProduceExtraEscape()
    {
        // "\%" -> backslash escaped to "\\", then the literal "%" escaped to "\%": "\\\%".
        // If wildcards were escaped before the backslash, the "\%" this step introduces would
        // wrongly be re-escaped into "\\%" on a second backslash pass.
        Assert.Equal("\\\\\\%", SearchRules.EscapeLike("\\%"));
    }
}
