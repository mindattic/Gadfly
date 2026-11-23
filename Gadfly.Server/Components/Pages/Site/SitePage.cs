using Gadfly.Server.Localization;
using Microsoft.AspNetCore.Components;

namespace Gadfly.Server.Components.Pages.Site;

/// <summary>
/// A public site page in the visitor's chosen language (GAD-T592): its text comes from
/// <c>Localization/Strings/site.&lt;lang&gt;.json</c> through <see cref="T"/>. <see cref="H"/> renders a string that carries
/// inline markup (strong, em, links); those strings are Gadfly's own embedded files, never user input.
/// </summary>
public abstract class SitePage : ComponentBase
{
    [Inject] protected UiText T { get; set; } = default!;

    /// <summary>A site string with inline markup, rendered as markup.</summary>
    protected MarkupString H(string key) => new(T[key].Value);

    /// <summary>The lang attribute for content kept in English on a page shown in another language (WCAG 3.1.2), else null.</summary>
    protected string? EnglishOnly => T.IsEnglish ? null : "en";
}
