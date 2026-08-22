namespace MedHistory;

/// <summary>
/// Marker type for the cross-area vocabulary — the anchor
/// <c>IStringLocalizer&lt;SharedResource&gt;</c> / <c>IHtmlLocalizer&lt;SharedResource&gt;</c>
/// resolve against. It has no members and is never instantiated; only its full name matters,
/// because that is what the localizer factory turns into a resource base name.
///
/// It must stay in the root namespace and at the project root. The factory strips the assembly's
/// root namespace off the type name and prefixes the configured <c>ResourcesPath</c>, so
/// <c>MedHistory.SharedResource</c> resolves to <c>MedHistory.Resources.SharedResource</c> — that
/// is, <c>Resources/SharedResource.&lt;culture&gt;.resx</c>. Moved into a folder or a nested
/// namespace it would still compile, and every shared string would silently fall back to English.
/// </summary>
public class SharedResource
{
}
