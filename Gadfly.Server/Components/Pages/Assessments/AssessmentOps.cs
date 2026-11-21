using Gadfly.Server.Assessments;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Components.Pages.Assessments;

/// <summary>
/// Assessment setup data access for components (GAD-T85). Every operation runs in its own DI scope acting as
/// the circuit's pinned tenant (GAD-T73), so a page never shares the circuit's DbContext with the layout or
/// another in-flight query.
/// </summary>
internal static class AssessmentOps
{
    public static async Task<T> RunAsync<T>(IServiceScopeFactory scopes, ITenantContext tenant, Func<AssessmentService, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScopeFor(TenantSnapshot.Of(tenant));
        return await work(ActivatorUtilities.GetServiceOrCreateInstance<AssessmentService>(scope.ServiceProvider));
    }

    public static string When(DateTimeOffset? at) => at is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC" : "—";

    public static string ModeLabel(Gadfly.Assessment.AssessmentMode m) => m == Gadfly.Assessment.AssessmentMode.Examination ? "Examination" : "Learning";

    public static string DimensionLabel(Gadfly.Assessment.AssessmentDimension d) => d switch
    {
        Gadfly.Assessment.AssessmentDimension.FactualKnowledge => "Factual knowledge",
        Gadfly.Assessment.AssessmentDimension.ConceptualUnderstanding => "Conceptual understanding",
        Gadfly.Assessment.AssessmentDimension.LogicalReasoning => "Logical reasoning",
        Gadfly.Assessment.AssessmentDimension.Application => "Application",
        Gadfly.Assessment.AssessmentDimension.KnowledgeTransfer => "Knowledge transfer",
        Gadfly.Assessment.AssessmentDimension.MisconceptionRecognition => "Misconception recognition",
        Gadfly.Assessment.AssessmentDimension.Consistency => "Consistency",
        Gadfly.Assessment.AssessmentDimension.Metacognition => "Metacognition",
        _ => "Custom (subject-specific)",
    };
}
