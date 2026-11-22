namespace Gadfly.Server.Data;

// LTI 1.3 (GAD-T312): a registration per connected platform (Canvas, Moodle, Blackboard, Brightspace...), Gadfly's own
// signing key, the short-lived OIDC login state, the placement-to-exam mapping deep linking creates, the LTI-subject-
// to-Gadfly-user mapping roster sync and launches both feed, and the assignment-to-placement link grade passback reads.

/// <summary>
/// Gadfly's own RSA key pair for signing LTI JWTs (the deep-linking response, and the client-assertion used for
/// AGS/NRPS token requests): one active row, generated on first use. Not tenant-owned: one tool identity serves every
/// organization, the way a single JWKS document is expected to. The private key is stored only in its data-protected
/// form (<see cref="Lti.LtiKeys"/>), the same discipline <see cref="Webhook.ProtectedSecret"/> uses.
/// </summary>
public sealed class LtiToolKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The JWKS <c>kid</c>: a short random id, never the key material.</summary>
    public string Kid { get; set; } = "";
    /// <summary>The RSA private key (PKCS#8 DER), protected with <see cref="Microsoft.AspNetCore.DataProtection.IDataProtector"/>.</summary>
    public string ProtectedPrivateKey { get; set; } = "";
    /// <summary>The public key as a JWKS <c>keys[]</c> entry (kty, n, e, kid, use, alg), served as-is at <c>/lti/jwks.json</c>.</summary>
    public string PublicJwkJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One platform connection (GAD-T312/GAD-T380): the facts the platform's own "External Tool"/"Developer Key"
/// registration screen gives an administrator back, which they paste in here (<c>Admin/LtiSetup</c>, GAD-T384).
/// Gadfly never invents these; it only names where the platform should point (<c>/lti/login</c>, <c>/lti/launch</c>,
/// <c>/lti/jwks.json</c>). One organization may hold more than one row (more than one LMS, or more than one
/// deployment of the same LMS); a deployment id names exactly one row.
/// </summary>
public sealed class LtiRegistration : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    /// <summary>A label for the admin page, e.g. "Canvas - Acme University".</summary>
    public string Name { get; set; } = "";
    /// <summary>The platform's issuer (<c>iss</c> on every launch).</summary>
    public string Issuer { get; set; } = "";
    /// <summary>The client id the platform assigned this tool (checked as <c>aud</c>/<c>azp</c>).</summary>
    public string ClientId { get; set; } = "";
    /// <summary>The platform's deployment id for this installation (the <c>...claim/deployment_id</c> claim).</summary>
    public string DeploymentId { get; set; } = "";
    /// <summary>The platform's OIDC third-party-initiated-login authorization endpoint.</summary>
    public string AuthLoginUrl { get; set; } = "";
    /// <summary>The platform's OAuth2 token endpoint (client-credentials grant, for NRPS/AGS calls).</summary>
    public string AuthTokenUrl { get; set; } = "";
    /// <summary>The platform's JWKS URL, to verify the launch JWT's signature.</summary>
    public string KeySetUrl { get; set; } = "";
    /// <summary>The <c>aud</c> Gadfly's client assertion must name when the platform wants something other than its
    /// token URL (Brightspace: <c>https://api.brightspace.com/auth/token</c>); null means the token URL (GAD-T383).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string? TokenAudience { get; set; }
    public bool Enabled { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The OIDC third-party-initiated-login's state, for the few minutes between <c>/lti/login</c> redirecting the
/// browser to the platform and the platform redirecting it back to <c>/lti/launch</c> (GAD-T380). Not tenant-owned:
/// looked up before any tenant is known (the visitor isn't signed in to Gadfly yet). <see cref="State"/> is the
/// OIDC <c>state</c> parameter itself (a long random token), so the lookup IS the CSRF check.
/// </summary>
public sealed class LtiLoginState
{
    public string State { get; set; } = "";
    public Guid OrganizationId { get; set; }
    public Guid RegistrationId { get; set; }
    /// <summary>Checked against the launch JWT's <c>nonce</c> claim; consumed (see <see cref="Used"/>) so a captured launch can't replay.</summary>
    public string Nonce { get; set; } = "";
    /// <summary>The platform's <c>target_link_uri</c>, carried through so the launch can tell what was being opened.</summary>
    public string TargetLinkUri { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
}

/// <summary>
/// One placement of one Gadfly exam in one LMS course (GAD-T312/GAD-T381): created by the deep-linking response (an
/// instructor picked an exam for this course's assignment), then matched by every later launch of that same
/// placement (<c>context_id</c> + <c>resource_link_id</c>, scoped to the registration). The AGS endpoint fields are
/// filled in from whichever launch first carries the <c>...claim/endpoint</c> claim, and are the destination grade
/// passback (GAD-T383) writes to.
/// </summary>
public sealed class LtiResourceLink : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid RegistrationId { get; set; }
    /// <summary>The LMS course/context id (<c>...claim/context</c>'s <c>id</c>).</summary>
    public string ContextId { get; set; } = "";
    /// <summary>The LMS placement's own id (<c>...claim/resource_link</c>'s <c>id</c>).</summary>
    public string ResourceLinkId { get; set; } = "";
    public Guid ExamId { get; set; }
    /// <summary>AGS: where to create a line item (the <c>lineitems</c> collection URL), when known.</summary>
    public string? LineItemsUrl { get; set; }
    /// <summary>AGS: this placement's own line item (created lazily on first passback if the launch gave none directly).</summary>
    public string? LineItemUrl { get; set; }
    /// <summary>NRPS: the roster endpoint for this context, when a launch carried it.</summary>
    public string? ContextMembershipsUrl { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastRosterSyncAt { get; set; }
}

/// <summary>
/// Maps one platform's notion of a person (<c>sub</c> on every launch, stable per platform/deployment) to the Gadfly
/// account auto-provisioned for them (GAD-T312/GAD-T382): launches and NRPS roster rows both resolve through this,
/// so the same person is recognized whether they click into Gadfly themselves or are only ever read from the roster.
/// </summary>
public sealed class LtiUserMapping : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid RegistrationId { get; set; }
    public string LtiSubject { get; set; } = "";
    public Guid GadflyUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Which placement created one <see cref="ExamAssignment"/> (GAD-T312/GAD-T383): grade passback needs to know which
/// line item to write a released score to, and an assignment may exist without ever having gone through LTI (a
/// directly-assigned student), so this is a side table rather than a column on the shared <see cref="ExamAssignment"/>.
/// </summary>
public sealed class LtiAssignmentLink : ITenantOwned
{
    public Guid AssignmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ResourceLinkId { get; set; }
    public string LtiSubject { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When grade passback last sent a score for this assignment's released result (GAD-T383); null until then.
    /// A polling sweeper (<see cref="Lti.LtiPassbackSweeper"/>), not a direct hook off publication, drives passback
    /// (GAD-T312 simplification: a short delay, not the instant of release). A score is sent once per released
    /// result; an amendment that changes the percentage differs from <see cref="LastPassbackPercent"/>, so the new
    /// score goes out too.</summary>
    public DateTimeOffset? PassbackSentAt { get; set; }
    public decimal? LastPassbackPercent { get; set; }
}
