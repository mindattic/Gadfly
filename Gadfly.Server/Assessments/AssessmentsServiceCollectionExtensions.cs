namespace Gadfly.Server.Assessments;

public static class AssessmentsServiceCollectionExtensions
{
    /// <summary>Teacher assessment setup and grading policies (GAD-T82/T85). Components open their own scope per operation.</summary>
    public static IServiceCollection AddAssessments(this IServiceCollection services)
    {
        services.AddScoped<AssessmentService>();
        return services;
    }
}
