using MedHistory.Services;

namespace MedHistory.Tests;

public class PhotoRulesTests
{
    // ---- Validate: length ----

    [Fact]
    public void Validate_ZeroLength_ReturnsError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: 0);

        Assert.Contains(errors, e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_NegativeLength_ReturnsError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: -1);

        Assert.Contains(errors, e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_LengthAtMax_NoLengthError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: PhotoRules.MaxSizeBytes);

        Assert.DoesNotContain(errors, e => e.Contains("empty"));
        Assert.DoesNotContain(errors, e => e.Contains("exceeds"));
    }

    [Fact]
    public void Validate_LengthOverMax_ReturnsError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: PhotoRules.MaxSizeBytes + 1);

        Assert.Contains(errors, e => e.Contains("exceeds"));
    }

    // ---- Validate: content type ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NullOrEmptyContentType_ReturnsError(string? contentType)
    {
        var errors = PhotoRules.Validate(contentType, length: 1);

        Assert.Contains(errors, e => e.Contains("image file"));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("video/mp4")]
    public void Validate_NonImageContentType_ReturnsError(string contentType)
    {
        var errors = PhotoRules.Validate(contentType, length: 1);

        Assert.Contains(errors, e => e.Contains("image file"));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/PNG")]
    public void Validate_ImageContentType_CaseInsensitive_NoContentTypeError(string contentType)
    {
        var errors = PhotoRules.Validate(contentType, length: 1);

        Assert.DoesNotContain(errors, e => e.Contains("image file"));
    }

    // ---- Validate: valid file ----

    [Fact]
    public void Validate_ValidFile_ReturnsNoErrors()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: PhotoRules.MaxSizeBytes);

        Assert.Empty(errors);
    }
}
