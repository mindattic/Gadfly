namespace Gadfly.Socrates;

/// <summary>
/// What kind of AI panel this process runs on (docs/MASTER-PLAN.md Part 22, GAD-T100). Implemented by the
/// <see cref="ISocraticVotingClient"/> the host registers (<c>services.AddGadflyPanel(configuration)</c>), so code
/// holding the panel can ask <c>panel is IPanelInfo { IsMock: true }</c>, and resolvable from DI on its own for
/// pages (the MOCK banner). Anything produced while <see cref="IsMock"/> is true is a simulation: it must be
/// stored flagged as mock and never shown or used as a genuine AI evaluation.
/// </summary>
public interface IPanelInfo
{
    /// <summary>True when the panel is the deterministic mock (no LLM provider is called).</summary>
    bool IsMock { get; }

    /// <summary>The effective mode: <see cref="PanelModes.Live"/> or <see cref="PanelModes.Mock"/>.</summary>
    string Mode { get; }

    /// <summary>Why this mode is in effect (configured, or what the auto probe found), for logs and the banner.</summary>
    string Reason { get; }
}

/// <summary>The values of <c>Gadfly:AiMode</c>.</summary>
public static class PanelModes
{
    public const string Setting = "Gadfly:AiMode";

    /// <summary>Real providers through MindAttic.Legion.</summary>
    public const string Live = "live";

    /// <summary>The deterministic, clearly labelled mock panel (no keys needed).</summary>
    public const string Mock = "mock";

    /// <summary>Live when at least one provider answers a probe at startup, otherwise mock.</summary>
    public const string Auto = "auto";

    /// <summary>The configured mode, normalized; unknown values are refused rather than guessed.</summary>
    public static string Parse(string? value, string fallback) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "" => fallback,
        Live => Live,
        Mock => Mock,
        Auto => Auto,
        var other => throw new InvalidOperationException($"{Setting} is \"{other}\"; use \"live\", \"mock\" or \"auto\"."),
    };
}
