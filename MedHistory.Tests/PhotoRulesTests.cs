using MedHistory.Services;

namespace MedHistory.Tests;

public class PhotoRulesTests
{
    // The keys the rules hand back. Asserted by key rather than by a fragment of the sentence:
    // the key is the contract now — it is what the .resx is indexed by — so a reworded message
    // has to be reworded here too, where a substring match would have gone on passing.
    private const string Empty = "Photo file is empty.";
    private const string TooBig = "Photo exceeds the {0} MB limit.";
    private const string NotAnImage = "Photo must be an image file.";

    // ---- Validate: length ----

    [Fact]
    public void Validate_ZeroLength_ReturnsError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: 0);

        Assert.Contains(errors, e => e.Key == Empty);
    }

    [Fact]
    public void Validate_NegativeLength_ReturnsError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: -1);

        Assert.Contains(errors, e => e.Key == Empty);
    }

    [Fact]
    public void Validate_LengthAtMax_NoLengthError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: PhotoRules.MaxSizeBytes);

        Assert.DoesNotContain(errors, e => e.Key == Empty);
        Assert.DoesNotContain(errors, e => e.Key == TooBig);
    }

    [Fact]
    public void Validate_LengthOverMax_ReturnsError()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: PhotoRules.MaxSizeBytes + 1);

        var error = Assert.Single(errors, e => e.Key == TooBig);

        // The limit is a hole rather than part of the sentence, so the number has to still be
        // in the arguments for a translation to have anywhere to put it.
        Assert.Equal(new object[] { PhotoRules.MaxSizeBytes / (1024 * 1024) }, error.Args);
        Assert.Equal("Photo exceeds the 10 MB limit.", error.Text);
    }

    // ---- Validate: content type ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NullOrEmptyContentType_ReturnsError(string? contentType)
    {
        var errors = PhotoRules.Validate(contentType, length: 1);

        Assert.Contains(errors, e => e.Key == NotAnImage);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("video/mp4")]
    public void Validate_NonImageContentType_ReturnsError(string contentType)
    {
        var errors = PhotoRules.Validate(contentType, length: 1);

        Assert.Contains(errors, e => e.Key == NotAnImage);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/PNG")]
    public void Validate_ImageContentType_CaseInsensitive_NoContentTypeError(string contentType)
    {
        var errors = PhotoRules.Validate(contentType, length: 1);

        Assert.DoesNotContain(errors, e => e.Key == NotAnImage);
    }

    // ---- Validate: valid file ----

    [Fact]
    public void Validate_ValidFile_ReturnsNoErrors()
    {
        var errors = PhotoRules.Validate("image/jpeg", length: PhotoRules.MaxSizeBytes);

        Assert.Empty(errors);
    }
}
