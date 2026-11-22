using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Gadfly.Server.Tenancy;
using Gadfly.Socrates;

namespace Gadfly.Server.Translation;

/// <summary>Several texts translated together: each distinct original maps to its translation.</summary>
public sealed record TranslationBatch(IReadOnlyDictionary<string, MachineTranslation> ByOriginal, bool Mock, string? Error)
{
    public static readonly TranslationBatch Empty = new(new Dictionary<string, MachineTranslation>(), false, null);

    /// <summary>The translation of <paramref name="original"/>, or null when it was not translated.</summary>
    public MachineTranslation? Of(string? original) => original is null ? null : ByOriginal.GetValueOrDefault(original);
}

/// <summary>
/// Machine translation run for the caller's organization (GAD-T540, GAD-T548): translates a batch of texts with
/// <see cref="MachineTranslator"/>, meters any live model call to the organization (and the session, when there is one) through
/// <see cref="UsageMeter"/> like every other AI call, and never stores the translations with the record they translate.
/// </summary>
public sealed class TranslationService(GadflyDbContext db, ITenantContext tenant, IPanelInfo panel, ILiveTurnModel model, UsageMeter meter)
{
    private readonly MachineTranslator translator = new(panel, model);

    /// <summary>The most distinct texts translated in one batch.</summary>
    public const int MaxTexts = 400;

    public bool IsMock => translator.IsMock;

    /// <summary>
    /// Translates every distinct non-blank text of <paramref name="texts"/>. A failure stops the batch and is reported in
    /// <see cref="TranslationBatch.Error"/> with whatever was translated before it; the caller shows the originals regardless.
    /// </summary>
    public async Task<TranslationBatch> TranslateAsync(IEnumerable<string?> texts, string from, string to, string purpose, Guid? sessionId = null,
        CancellationToken ct = default)
    {
        if (tenant.OrganizationId is not { } org) return TranslationBatch.Empty with { Error = "No active organization." };
        // Staff only: graders (who read transcripts) and those who set up exams. A student never gets the translation service.
        if (!Gadfly.Assessment.PermissionMatrix.Has(tenant.Role, Gadfly.Assessment.Permission.ViewTranscripts)
            && !Gadfly.Assessment.PermissionMatrix.Has(tenant.Role, Gadfly.Assessment.Permission.ManageAssessments))
            return TranslationBatch.Empty with { Error = "Your role in this organization does not include machine translation." };
        var distinct = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).Distinct(StringComparer.Ordinal).Take(MaxTexts).ToList();
        var result = new Dictionary<string, MachineTranslation>(StringComparer.Ordinal);
        string? error = null;
        IReadOnlyList<PanelCall> calls;
        using (PanelUsage.Track(out calls))
        {
            foreach (var text in distinct)
            {
                try { result[text] = await translator.TranslateAsync(text, from, to, ct); }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    error = "The machine translation is not available right now; the originals are shown. Try again in a moment.";
                    break;
                }
            }
        }
        if (calls.Count > 0)
        {
            meter.Stage(db, org, sessionId, calls, purpose);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return new TranslationBatch(result, translator.IsMock, error);
    }
}
