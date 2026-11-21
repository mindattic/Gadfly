using Gadfly.Server.Data;
using Gadfly.Socrates;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Platform;

/// <summary>Price of one provider's tokens, USD per million.</summary>
public sealed record TokenPrice(decimal InputPerMillion, decimal OutputPerMillion);

/// <summary>
/// Turns metered panel calls into UsageEntry rows with an estimated cost (GAD-T47). Prices come from
/// configuration (<c>Gadfly:Prices:&lt;provider&gt;:InputPerMillion</c> / <c>OutputPerMillion</c>, optionally
/// per model as <c>Gadfly:Prices:&lt;provider&gt;:&lt;model&gt;:...</c>); a provider with no configured price
/// uses the rough fallback below. Every figure is an estimate until Legion reports real token usage
/// (M.LEGN-T50), and the admin console says so.
/// </summary>
public sealed class UsageMeter(IConfiguration configuration, TimeProvider clock)
{
    /// <summary>Rough fallbacks (USD per million tokens) used only when nothing is configured.</summary>
    private static readonly IReadOnlyDictionary<string, TokenPrice> Fallback = new Dictionary<string, TokenPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = new(3m, 15m),
        ["openai"] = new(2.5m, 10m),
        ["gemini"] = new(1.25m, 5m),
        ["deepseek"] = new(0.3m, 1.2m),
    };

    public TokenPrice PriceOf(string provider, string model)
    {
        var perModel = configuration.GetSection($"Gadfly:Prices:{provider}:{model}");
        var perProvider = configuration.GetSection($"Gadfly:Prices:{provider}");
        TokenPrice? Read(IConfigurationSection s) =>
            decimal.TryParse(s["InputPerMillion"], System.Globalization.CultureInfo.InvariantCulture, out var i)
            && decimal.TryParse(s["OutputPerMillion"], System.Globalization.CultureInfo.InvariantCulture, out var o)
                ? new TokenPrice(i, o) : null;
        return Read(perModel) ?? Read(perProvider) ?? Fallback.GetValueOrDefault(provider) ?? new TokenPrice(1m, 4m);
    }

    /// <summary>Adds usage rows for <paramref name="calls"/> to <paramref name="db"/> (not saved), one per provider/model/kind.</summary>
    public void Stage(GadflyDbContext db, Guid organizationId, Guid? sessionId, IReadOnlyList<PanelCall> calls, string purpose)
    {
        List<PanelCall> snapshot;
        lock (calls) snapshot = [.. calls];
        var now = clock.GetUtcNow();
        foreach (var g in snapshot.GroupBy(c => (c.ProviderId, c.Model, c.Kind)))
        {
            var input = g.Sum(c => c.InputTokens);
            var output = g.Sum(c => c.OutputTokens);
            var price = PriceOf(g.Key.ProviderId, g.Key.Model);
            db.UsageEntries.Add(new UsageEntry
            {
                OrganizationId = organizationId,
                SessionId = sessionId,
                Kind = UsageKind.Llm,
                Provider = g.Key.ProviderId,
                Model = g.Key.Model.Length > 120 ? g.Key.Model[..120] : g.Key.Model,
                InputUnits = input,
                OutputUnits = output,
                CostUsd = Math.Round((input * price.InputPerMillion + output * price.OutputPerMillion) / 1_000_000m, 6),
                Purpose = $"{purpose}:{g.Key.Kind}"[..Math.Min(60, purpose.Length + 1 + g.Key.Kind.Length)],
                CreatedAt = now,
            });
        }
    }

    /// <summary>This calendar month's estimated spend for the organization, and its quota if one is set.</summary>
    public async Task<(decimal Spent, decimal? Quota)> MonthAsync(GadflyDbContext db, Guid organizationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var spent = await db.UsageEntries.IgnoreQueryFilters()
            .Where(u => u.OrganizationId == organizationId && u.CreatedAt >= start).SumAsync(u => (decimal?)u.CostUsd, ct) ?? 0m;
        var quota = await db.OrganizationSettings.Where(s => s.OrganizationId == organizationId).Select(s => s.MonthlyCostQuotaUsd).FirstOrDefaultAsync(ct);
        return (spent, quota);
    }
}
