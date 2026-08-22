namespace MedHistory.Services;

/// <summary>
/// Pure photo upload rules — no HTTP, no database. Controllers call these so
/// the same decisions can be unit tested without spinning up the app.
/// </summary>
public static class PhotoRules
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Returns one message per broken rule; an empty list means the upload is valid.
    /// </summary>
    public static IReadOnlyList<RuleMessage> Validate(string? contentType, long length)
    {
        var errors = new List<RuleMessage>();

        if (length <= 0)
        {
            errors.Add("Photo file is empty.");
        }
        else if (length > MaxSizeBytes)
        {
            errors.Add(new RuleMessage("Photo exceeds the {0} MB limit.", MaxSizeBytes / (1024 * 1024)));
        }

        if (string.IsNullOrWhiteSpace(contentType) ||
            !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Photo must be an image file.");
        }

        return errors;
    }
}
