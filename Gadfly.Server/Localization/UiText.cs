using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Gadfly.Server.Localization;

/// <summary>
/// The interface text of one circuit or request, in its UI language (GAD-T543). Scoped: it starts in the request's culture (the
/// language cookie, set by the language switch; English when there is none) and a page can switch it live, which re-renders
/// every component listening to <see cref="Changed"/> without a reload (the get-ready page's language picker, GAD-T546).
/// <para>
/// <c>T["key"]</c> is the text in the current language, English when that language has no translation yet, and the key itself
/// only for a key nobody defined. <c>T["key", args]</c> formats with the language's culture. An exam attempt is conducted in a
/// fixed language whatever the switch says: <see cref="For"/> gives the text of that language (the exam room, GAD-T544).
/// </para>
/// </summary>
public sealed class UiText : IStringLocalizer
{
    private readonly UiCatalog catalog;

    public UiText(UiCatalog catalog) : this(catalog, CultureInfo.CurrentUICulture.Name) { }

    public UiText(UiCatalog catalog, string language)
    {
        this.catalog = catalog;
        Language = UiCatalog.Normalize(language);
    }

    /// <summary>The UI language: "en" or "ko".</summary>
    public string Language { get; private set; }

    /// <summary>The culture for dates and numbers in <see cref="Language"/>.</summary>
    public CultureInfo Culture => UiCatalog.CultureFor(Language);

    /// <summary>True when the UI is in English (the source language).</summary>
    public bool IsEnglish => Language == UiCatalog.English;

    /// <summary>Raised after <see cref="Use"/> changes the language; listeners re-render.</summary>
    public event Action? Changed;

    /// <summary>Switches this circuit's UI language; no-op when it is already that language.</summary>
    public void Use(string? language)
    {
        var lang = UiCatalog.Normalize(language);
        if (lang == Language) return;
        Language = lang;
        Changed?.Invoke();
    }

    /// <summary>The text of a fixed language, independent of this circuit's switch (an attempt's language, an email's recipient).</summary>
    public UiText For(string? language) => new(catalog, language ?? UiCatalog.English);

    public LocalizedString this[string name]
    {
        get
        {
            var s = catalog.Find(Language, name);
            return new LocalizedString(name, s ?? name, resourceNotFound: s is null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var s = catalog.Find(Language, name);
            var text = s is null ? name : string.Format(Culture, s, arguments);
            return new LocalizedString(name, text, resourceNotFound: s is null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        catalog.Dictionary(Language).Select(kv => new LocalizedString(kv.Key, kv.Value));

    /// <summary>Every key under <paramref name="prefix"/>, for a JS module (English fills untranslated keys).</summary>
    public IReadOnlyDictionary<string, string> Dictionary(string prefix) => catalog.Dictionary(Language, prefix);

    /// <summary>A date and time in this language's style, in UTC (the server keeps every time in UTC).</summary>
    public string DateTime(DateTimeOffset t) => IsEnglish
        ? t.ToUniversalTime().ToString("ddd d MMM yyyy, HH:mm", Culture) + " UTC"
        : t.ToUniversalTime().ToString("yyyy년 M월 d일 (ddd) HH:mm", Culture) + " UTC";

    /// <summary>A date in this language's style.</summary>
    public string Date(DateTimeOffset t) => IsEnglish
        ? t.ToUniversalTime().ToString("d MMM yyyy", Culture)
        : t.ToUniversalTime().ToString("yyyy년 M월 d일", Culture);

    /// <summary>A number in this language's style ("0.##" by default).</summary>
    public string Number(double n, string format = "0.##") => n.ToString(format, Culture);
}
