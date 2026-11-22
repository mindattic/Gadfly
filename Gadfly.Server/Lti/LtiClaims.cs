namespace Gadfly.Server.Lti;

/// <summary>
/// LTI 1.3 claim URIs (IMS Global Learning Tools Interoperability Core, Deep Linking, Names and Roles Provisioning
/// and Assignment and Grade Services specifications) and the message/role/scope constants this integration reads.
/// Nothing here is Gadfly-specific; it is the vocabulary every platform (Canvas, Moodle, Blackboard, Brightspace)
/// speaks on the wire.
/// </summary>
public static class LtiClaims
{
    public const string MessageType = "https://purl.imsglobal.org/spec/lti/claim/message_type";
    public const string Version = "https://purl.imsglobal.org/spec/lti/claim/version";
    public const string DeploymentId = "https://purl.imsglobal.org/spec/lti/claim/deployment_id";
    public const string TargetLinkUri = "https://purl.imsglobal.org/spec/lti/claim/target_link_uri";
    public const string ResourceLink = "https://purl.imsglobal.org/spec/lti/claim/resource_link";
    public const string Context = "https://purl.imsglobal.org/spec/lti/claim/context";
    public const string Roles = "https://purl.imsglobal.org/spec/lti/claim/roles";
    public const string Custom = "https://purl.imsglobal.org/spec/lti/claim/custom";
    public const string Lis = "https://purl.imsglobal.org/spec/lti/claim/lis";

    public const string DeepLinkingSettings = "https://purl.imsglobal.org/spec/lti-dl/claim/deep_linking_settings";
    public const string DeepLinkingContentItems = "https://purl.imsglobal.org/spec/lti-dl/claim/content_items";
    public const string DeepLinkingData = "https://purl.imsglobal.org/spec/lti-dl/claim/data";

    public const string NamesRoleService = "https://purl.imsglobal.org/spec/lti-nrps/claim/namesroleservice";
    public const string AgsEndpoint = "https://purl.imsglobal.org/spec/lti-ags/claim/endpoint";

    public const string ResourceLinkRequest = "LtiResourceLinkRequest";
    public const string DeepLinkingRequest = "LtiDeepLinkingRequest";
    public const string DeepLinkingResponse = "LtiDeepLinkingResponse";
    public const string SupportedVersion = "1.3.0";

    /// <summary>The only signing algorithm LTI 1.3 (IMS Security Framework section 6.1) allows on the wire.</summary>
    public const string Algorithm = "RS256";

    // ---- roles (the standard LIS vocabulary a launch's roles[] array, or an NRPS member's roles, names).

    public const string RoleInstructor = "http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor";
    public const string RoleTeachingAssistant = "http://purl.imsglobal.org/vocab/lis/v2/membership/Instructor#TeachingAssistant";
    public const string RoleContentDeveloper = "http://purl.imsglobal.org/vocab/lis/v2/membership#ContentDeveloper";
    public const string RoleLearner = "http://purl.imsglobal.org/vocab/lis/v2/membership#Learner";

    private const string MembershipPrefix = "http://purl.imsglobal.org/vocab/lis/v2/membership";

    /// <summary>
    /// The context (course) role a single LTI role string names, or null for a system/institution role or anything
    /// else that says nothing about this course. Handles every form the specification allows (LTI Core section A.2.3):
    /// the full URI (<c>...membership#Instructor</c>), a sub-role URI whose principal role is what counts
    /// (<c>...membership/Instructor#TeachingAssistant</c> is an Instructor), and the short names NRPS and some platforms
    /// send (<c>Instructor</c>, <c>Learner</c>). Institution roles (<c>...institution/person#Student</c>) are ignored on
    /// purpose: being a student of the university says nothing about this course.
    /// </summary>
    public static string? ContextRoleOf(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        role = role.Trim();
        if (role.StartsWith(MembershipPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = role[MembershipPrefix.Length..];
            if (rest.StartsWith('#')) return rest[1..];                                   // membership#Instructor
            if (rest.StartsWith('/')) { var p = rest[1..]; var hash = p.IndexOf('#'); return hash < 0 ? p : p[..hash]; }   // membership/Instructor#TA
            return null;
        }
        if (role.Contains("://", StringComparison.Ordinal) || role.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)) return null;   // system/institution/other vocabularies
        return role.Contains('#') ? role[..role.IndexOf('#')] : role;                                                                      // short form
    }

    /// <summary>
    /// Maps a launch's or roster row's roles to a Gadfly organization role (GAD-T382): Instructor, TeachingAssistant,
    /// ContentDeveloper and Administrator course roles become <see cref="Tenancy.OrgRoles.Instructor"/>, Learner becomes
    /// <see cref="Tenancy.OrgRoles.Student"/>, and staff wins when both appear (a TA enrolled as a student too). Null
    /// when nothing maps (a Mentor, an observer, or only institution roles): such a person is refused, never guessed
    /// into a role. Never Owner or Admin: an LMS cannot make anyone an administrator of the Gadfly organization.
    /// </summary>
    public static string? GadflyRoleOf(IEnumerable<string>? ltiRoles)
    {
        var context = (ltiRoles ?? []).Select(ContextRoleOf).Where(r => r is not null).Select(r => r!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (context.Overlaps(["Instructor", "TeachingAssistant", "ContentDeveloper", "Administrator"])) return Tenancy.OrgRoles.Instructor;
        if (context.Contains("Learner")) return Tenancy.OrgRoles.Student;
        return null;
    }

    // ---- OAuth2 scopes (client-credentials grant) this integration asks for.

    public const string ScopeNrps = "https://purl.imsglobal.org/spec/lti-nrps/scope/contextmembership.readonly";
    public const string ScopeAgsScore = "https://purl.imsglobal.org/spec/lti-ags/scope/score";
    public const string ScopeAgsLineItem = "https://purl.imsglobal.org/spec/lti-ags/scope/lineitem";
}
