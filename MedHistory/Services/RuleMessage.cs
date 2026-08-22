using System.Globalization;

namespace MedHistory.Services;

/// <summary>
/// A message a pure rule produced, kept as its resource key plus the values the key's holes take
/// rather than as a finished sentence.
///
/// The key is the English source text, the same contract every other resource in the app follows,
/// so the rules go on speaking English and go on being testable without a localizer — whoever
/// surfaces the message is what looks the key up, the way <see cref="CultureRules.LanguageName"/>
/// and <see cref="AnxietyRules.Label"/> already hand back a key instead of copy.
///
/// The holes have to be numbered rather than interpolated, because Thai moves them: the type name
/// in "ต้องระบุความรุนแรงสำหรับบันทึกประเภท {0}" does not sit where English puts it, and an
/// interpolated string has already collapsed into one sentence with nowhere to put a translation
/// at all. A message with no values is just its key, which is why a plain string converts
/// implicitly — most rules never needed a hole and read exactly as they did before.
/// </summary>
public readonly record struct RuleMessage
{
    public RuleMessage(string key) : this(key, [])
    {
    }

    public RuleMessage(string key, params object[] args)
    {
        Key = key;
        Args = args;
    }

    /// <summary>The English source text, holes and all — which is also the resource key.</summary>
    public string Key { get; }

    /// <summary>What <c>{0}</c>, <c>{1}</c>… stand for, in order.</summary>
    public object[] Args { get; }

    /// <summary>
    /// The sentence in English, for a caller with no localizer to hand — which in practice means
    /// the tests. Invariant because the only values that ever reach a hole are names and small
    /// counts; the reader's own copy comes from the localizer, which does its own formatting.
    /// </summary>
    public string Text => Args.Length == 0
        ? Key
        : string.Format(CultureInfo.InvariantCulture, Key, Args);

    /// <summary>A message that carries no values is indistinguishable from its key.</summary>
    public static implicit operator RuleMessage(string key) => new(key);

    public override string ToString() => Text;
}
