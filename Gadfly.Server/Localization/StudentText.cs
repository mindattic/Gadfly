using System.Globalization;

namespace Gadfly.Server.Localization;

/// <summary>
/// A sentence a student reads, kept language-neutral (GAD-T628): a string key and its arguments, worded in a language only where it
/// is shown. <see cref="English"/> is the en.json rendering, byte-identical to the literal it replaced, so logs, the staff live
/// monitor and API callers keep reading English; a student page renders it in the attempt's language with <see cref="In"/>.
/// </summary>
public sealed class StudentText(string key, params object[] args)
{
    /// <summary>The key in Localization/Strings (refusal.* lives in refusal.&lt;lang&gt;.json).</summary>
    public string Key { get; } = key;

    /// <summary>The values of the key's <c>{0}</c> slots.</summary>
    public IReadOnlyList<object> Args { get; } = args;

    /// <summary>The English text from the embedded en strings (the key itself only when no file defines it).</summary>
    public string English => Render(UiCatalog.Embedded, UiCatalog.English);

    /// <summary>The text in <paramref name="text"/>'s language, English filling a gap.</summary>
    public string In(UiText text) => text[Key, [.. Args]].Value;

    /// <summary>The text in <paramref name="language"/> from <paramref name="catalog"/>, English filling a gap.</summary>
    public string Render(UiCatalog catalog, string language)
    {
        var s = catalog.Find(language, Key);
        return s is null ? Key : string.Format(UiCatalog.CultureFor(language), s, [.. Args]);
    }

    public override string ToString() => English;
}
