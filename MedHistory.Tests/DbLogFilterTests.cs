using MedHistory.Services;
using Microsoft.Extensions.Logging;

namespace MedHistory.Tests;

public class DbLogFilterTests
{
    private const string EfCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    // ---- ShouldWrite: the EF Core guard ----

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    public void ShouldWrite_EntityFrameworkBelowWarning_IsSkipped(LogLevel level)
    {
        Assert.False(DbLogFilter.ShouldWrite(EfCommandCategory, level));
    }

    [Fact]
    public void ShouldWrite_EntityFrameworkRootCategoryBelowWarning_IsSkipped()
    {
        Assert.False(DbLogFilter.ShouldWrite(DbLogFilter.EntityFrameworkCategoryPrefix, LogLevel.Information));
    }

    [Theory]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void ShouldWrite_EntityFrameworkAtWarningOrAbove_IsKept(LogLevel level)
    {
        Assert.True(DbLogFilter.ShouldWrite(EfCommandCategory, level));
    }

    [Fact]
    public void ShouldWrite_CategoryOnlyContainingEntityFrameworkPrefix_IsKept()
    {
        Assert.True(DbLogFilter.ShouldWrite("Contoso.Microsoft.EntityFrameworkCore.Thing", LogLevel.Information));
    }

    // ---- ShouldWrite: everything else passes through to the level filters ----

    [Fact]
    public void ShouldWrite_AppCategoryAtInformation_IsKept()
    {
        Assert.True(DbLogFilter.ShouldWrite("MedHistory.Controllers.EntriesController", LogLevel.Information));
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void ShouldWrite_NonEntityFrameworkCategory_IsKeptAtEveryLevel(LogLevel level)
    {
        Assert.True(DbLogFilter.ShouldWrite("Microsoft.AspNetCore.Hosting.Diagnostics", level));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShouldWrite_MissingCategory_IsKept(string? category)
    {
        Assert.True(DbLogFilter.ShouldWrite(category, LogLevel.Information));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("MedHistory.Controllers.EntriesController")]
    [InlineData(EfCommandCategory)]
    public void ShouldWrite_LevelNone_IsSkipped(string? category)
    {
        Assert.False(DbLogFilter.ShouldWrite(category, LogLevel.None));
    }

    // ---- Truncate ----

    [Fact]
    public void Truncate_Null_ReturnsNull()
    {
        Assert.Null(DbLogFilter.Truncate(null, 16));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("exactly-16-chars")]
    public void Truncate_WithinLimit_ReturnsValueUnchanged(string value)
    {
        Assert.Equal(value, DbLogFilter.Truncate(value, 16));
    }

    [Fact]
    public void Truncate_OverLimit_ClipsToMaxLength()
    {
        var value = new string('x', 20) + "tail";

        var truncated = DbLogFilter.Truncate(value, 16);

        Assert.Equal(new string('x', 16), truncated);
    }
}
