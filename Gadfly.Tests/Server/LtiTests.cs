using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gadfly.Server.Data;
using Gadfly.Server.Lti;
using Gadfly.Server.Tenancy;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// LTI 1.3 launch validation (GAD-T380): the signed id_token is accepted only when every check the IMS Security
/// Framework and LTI Core require holds. Pure: keys are generated in memory, no database, no network.
/// </summary>
[TestFixture]
public class LtiTokenValidatorTests
{
    private const string Issuer = "https://canvas.instructure.com";
    private const string ClientId = "10000000000042";
    private const string DeploymentId = "7:abc123";
    private const string Nonce = "n-0123456789abcdef";

    private static readonly RSA PlatformRsa = RSA.Create(2048);
    private static readonly RsaSecurityKey PlatformKey = new(PlatformRsa) { KeyId = "platform-1" };
    private static readonly RsaSecurityKey PlatformPublic = new(PlatformRsa.ExportParameters(false)) { KeyId = "platform-1" };

    private static readonly LtiExpectations Expect = new(Issuer, ClientId, DeploymentId, Nonce);

    [OneTimeTearDown]
    public void DisposeKeys() => PlatformRsa.Dispose();

    private static Dictionary<string, object> Claims(Action<Dictionary<string, object>>? change = null)
    {
        var now = DateTimeOffset.UtcNow;
        var c = new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = ClientId,
            ["sub"] = "user-42",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["nonce"] = Nonce,
            ["email"] = "ada@example.edu",
            ["name"] = "Ada Lovelace",
            [LtiClaims.Version] = "1.3.0",
            [LtiClaims.DeploymentId] = DeploymentId,
            [LtiClaims.MessageType] = LtiClaims.ResourceLinkRequest,
            [LtiClaims.Roles] = new[] { LtiClaims.RoleLearner },
            [LtiClaims.Context] = new Dictionary<string, object> { ["id"] = "course-1", ["label"] = "BIO101", ["title"] = "Biology" },
            [LtiClaims.ResourceLink] = new Dictionary<string, object> { ["id"] = "rl-9", ["title"] = "Osmosis oral" },
            [LtiClaims.AgsEndpoint] = new Dictionary<string, object>
            {
                ["scope"] = new[] { LtiClaims.ScopeAgsScore, LtiClaims.ScopeAgsLineItem },
                ["lineitems"] = "https://canvas.example.edu/api/lti/courses/1/line_items",
                ["lineitem"] = "https://canvas.example.edu/api/lti/courses/1/line_items/5",
            },
            [LtiClaims.NamesRoleService] = new Dictionary<string, object> { ["context_memberships_url"] = "https://canvas.example.edu/api/lti/courses/1/names_and_roles" },
            [LtiClaims.Custom] = new Dictionary<string, object> { ["examId"] = "3f2c6c55-0d7d-4b8e-9d55-1d1c1f9f2a10" },
        };
        change?.Invoke(c);
        return c;
    }

    private static string Sign(Dictionary<string, object> claims, SecurityKey? key = null, string alg = SecurityAlgorithms.RsaSha256) =>
        new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(claims), new SigningCredentials(key ?? PlatformKey, alg));

    private static Task<LtiTokenValidation> Validate(string token, LtiExpectations? expect = null) =>
        LtiTokenValidator.ValidateAsync(token, expect ?? Expect, [PlatformPublic]);

    [Test]
    public async Task AValidLaunch_IsAccepted_WithItsClaims()
    {
        var r = await Validate(Sign(Claims()));
        Assert.That(r.Ok, Is.True, r.Error);
        Assert.That(r.MessageType, Is.EqualTo(LtiClaims.ResourceLinkRequest));
        var c = r.Claims!;
        Assert.Multiple(() =>
        {
            Assert.That(c.Subject, Is.EqualTo("user-42"));
            Assert.That(c.Email, Is.EqualTo("ada@example.edu"));
            Assert.That(c.ContextId, Is.EqualTo("course-1"));
            Assert.That(c.ContextTitle, Is.EqualTo("Biology"));
            Assert.That(c.ResourceLinkId, Is.EqualTo("rl-9"));
            Assert.That(c.LineItemUrl, Does.EndWith("/line_items/5"));
            Assert.That(c.LineItemsUrl, Does.EndWith("/line_items"));
            Assert.That(c.ContextMembershipsUrl, Does.EndWith("/names_and_roles"));
            Assert.That(c.CustomExamId, Is.EqualTo(Guid.Parse("3f2c6c55-0d7d-4b8e-9d55-1d1c1f9f2a10")));
            Assert.That(c.GadflyRole, Is.EqualTo(OrgRoles.Student));
        });
    }

    [Test]
    public async Task SignedWithAnotherKey_IsRefused()
    {
        using var other = RSA.Create(2048);
        var r = await Validate(Sign(Claims(), new RsaSecurityKey(other) { KeyId = "platform-1" }));
        Assert.That(r.Ok, Is.False);
    }

    [Test]
    public async Task SignedWithAnUnknownKid_IsRefused()
    {
        using var other = RSA.Create(2048);
        var r = await LtiTokenValidator.ValidateAsync(Sign(Claims(), new RsaSecurityKey(other) { KeyId = "rotated-2" }), Expect, [PlatformPublic]);
        Assert.That(r.Ok, Is.False);
    }

    [Test]
    public async Task AnHmacToken_IsRefused_EvenWithTheRightSecretShape()
    {
        var secret = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)) { KeyId = "platform-1" };
        var r = await LtiTokenValidator.ValidateAsync(Sign(Claims(), secret, SecurityAlgorithms.HmacSha256), Expect, [PlatformPublic, secret]);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Does.Contain("RS256"));
    }

    [Test]
    public async Task AnUnsignedAlgNoneToken_IsRefused()
    {
        static string B64(string s) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(s));
        var token = B64("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." + B64(JsonSerializer.Serialize(Claims())) + ".";
        var r = await Validate(token);
        Assert.That(r.Ok, Is.False);
    }

    [Test]
    public async Task ATamperedPayload_IsRefused()
    {
        var parts = Sign(Claims()).Split('.');
        var forged = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Claims(c => c["sub"] = "someone-else"))));
        var r = await Validate($"{parts[0]}.{forged}.{parts[2]}");
        Assert.That(r.Ok, Is.False);
    }

    [Test]
    public async Task Garbage_IsRefused() =>
        Assert.That((await Validate("not-a-jwt")).Ok, Is.False);

    [TestCase("iss", "https://evil.example.com")]
    [TestCase("aud", "some-other-client")]
    [TestCase("nonce", "a-different-nonce")]
    [TestCase(LtiClaims.DeploymentId, "another-deployment")]
    [TestCase(LtiClaims.Version, "1.1")]
    public async Task AMismatchedClaim_IsRefused(string claim, string value)
    {
        var r = await Validate(Sign(Claims(c => c[claim] = value)));
        Assert.That(r.Ok, Is.False, claim);
    }

    [TestCase("sub")]
    [TestCase("nonce")]
    [TestCase("exp")]
    [TestCase("iat")]
    [TestCase(LtiClaims.MessageType)]
    [TestCase(LtiClaims.DeploymentId)]
    [TestCase(LtiClaims.Version)]
    public async Task AMissingRequiredClaim_IsRefused(string claim)
    {
        var r = await Validate(Sign(Claims(c => c.Remove(claim))));
        Assert.That(r.Ok, Is.False, claim);
    }

    [Test]
    public async Task AnExpiredLaunch_IsRefused()
    {
        var past = DateTimeOffset.UtcNow.AddMinutes(-30);
        var r = await Validate(Sign(Claims(c => { c["iat"] = past.ToUnixTimeSeconds(); c["exp"] = past.AddMinutes(5).ToUnixTimeSeconds(); })));
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Does.Contain("expired"));
    }

    [Test]
    public async Task SeveralAudiences_NeedAnAuthorizedPartyNamingThisClient()
    {
        var noAzp = await Validate(Sign(Claims(c => c["aud"] = new[] { ClientId, "another-tool" })));
        var wrongAzp = await Validate(Sign(Claims(c => { c["aud"] = new[] { ClientId, "another-tool" }; c["azp"] = "another-tool"; })));
        var rightAzp = await Validate(Sign(Claims(c => { c["aud"] = new[] { ClientId, "another-tool" }; c["azp"] = ClientId; })));
        Assert.Multiple(() =>
        {
            Assert.That(noAzp.Ok, Is.False);
            Assert.That(wrongAzp.Ok, Is.False);
            Assert.That(rightAzp.Ok, Is.True, rightAzp.Error);
        });
    }

    [Test]
    public async Task ADeepLinkingRequest_CarriesItsSettings()
    {
        var r = await Validate(Sign(Claims(c =>
        {
            c[LtiClaims.MessageType] = LtiClaims.DeepLinkingRequest;
            c[LtiClaims.Roles] = new[] { LtiClaims.RoleInstructor };
            c[LtiClaims.DeepLinkingSettings] = new Dictionary<string, object>
            {
                ["deep_link_return_url"] = "https://canvas.example.edu/courses/1/deep_linking_response",
                ["accept_types"] = new[] { "ltiResourceLink" },
                ["accept_multiple"] = false,
                ["data"] = "opaque-platform-data",
            };
        })));
        Assert.That(r.Ok, Is.True, r.Error);
        Assert.That(r.MessageType, Is.EqualTo(LtiClaims.DeepLinkingRequest));
        Assert.That(r.Claims!.DeepLinkingSettings!.Value.GetProperty("data").GetString(), Is.EqualTo("opaque-platform-data"));
        Assert.That(r.Claims.GadflyRole, Is.EqualTo(OrgRoles.Instructor));
    }
}

/// <summary>Role mapping (GAD-T382): LIS course roles to Gadfly organization roles.</summary>
[TestFixture]
public class LtiRoleMappingTests
{
    [TestCase(LtiClaims.RoleLearner, OrgRoles.Student)]
    [TestCase("Learner", OrgRoles.Student)]
    [TestCase(LtiClaims.RoleInstructor, OrgRoles.Instructor)]
    [TestCase("Instructor", OrgRoles.Instructor)]
    [TestCase(LtiClaims.RoleTeachingAssistant, OrgRoles.Instructor)]
    [TestCase("http://purl.imsglobal.org/vocab/lis/v2/membership#TeachingAssistant", OrgRoles.Instructor)]
    [TestCase(LtiClaims.RoleContentDeveloper, OrgRoles.Instructor)]
    [TestCase("http://purl.imsglobal.org/vocab/lis/v2/membership#Administrator", OrgRoles.Instructor)]
    public void ACourseRole_MapsToItsGadflyRole(string ltiRole, string expected) =>
        Assert.That(LtiClaims.GadflyRoleOf([ltiRole]), Is.EqualTo(expected));

    [Test]
    public void StaffWins_WhenBothAppear() =>
        Assert.That(LtiClaims.GadflyRoleOf([LtiClaims.RoleLearner, LtiClaims.RoleTeachingAssistant]), Is.EqualTo(OrgRoles.Instructor));

    [TestCase("http://purl.imsglobal.org/vocab/lis/v2/institution/person#Student")]
    [TestCase("http://purl.imsglobal.org/vocab/lis/v2/institution/person#Administrator")]
    [TestCase("http://purl.imsglobal.org/vocab/lis/v2/system/person#SysAdmin")]
    [TestCase("http://purl.imsglobal.org/vocab/lis/v2/membership#Mentor")]
    [TestCase("")]
    public void NoCourseRoleGadflyUses_MapsToNothing(string ltiRole) =>
        Assert.That(LtiClaims.GadflyRoleOf([ltiRole]), Is.Null);

    [Test]
    public void NeverOwnerOrAdmin()
    {
        var all = new[] { LtiClaims.RoleInstructor, "http://purl.imsglobal.org/vocab/lis/v2/membership#Administrator",
            "http://purl.imsglobal.org/vocab/lis/v2/institution/person#Administrator", "http://purl.imsglobal.org/vocab/lis/v2/system/person#Administrator" };
        Assert.That(LtiClaims.GadflyRoleOf(all), Is.Not.EqualTo(OrgRoles.Owner).And.Not.EqualTo(OrgRoles.Admin));
    }
}

/// <summary>Gadfly's own signed JWTs and the wire details of NRPS/AGS (GAD-T381/T383).</summary>
[TestFixture]
public class LtiWireTests
{
    private static readonly RSA ToolRsa = RSA.Create(2048);
    private static readonly SigningCredentials ToolCredentials = new(new RsaSecurityKey(ToolRsa) { KeyId = "tool-1" }, SecurityAlgorithms.RsaSha256);
    private static readonly RsaSecurityKey ToolPublic = new(ToolRsa.ExportParameters(false)) { KeyId = "tool-1" };

    [OneTimeTearDown]
    public void DisposeKeys() => ToolRsa.Dispose();

    private static readonly LtiRegistration Reg = new()
    {
        Issuer = "https://moodle.example.edu", ClientId = "client-abc", DeploymentId = "3",
        AuthTokenUrl = "https://moodle.example.edu/mod/lti/token.php",
    };

    [Test]
    public async Task TheDeepLinkingResponse_IsSignedByTheTool_ForThePlatform()
    {
        var examId = Guid.NewGuid();
        var jwt = LtiDeepLinking.BuildResponseJwt(ToolCredentials, Reg, "platform-data", [(examId, "Osmosis")], "https://gadfly.example.com/lti/launch", DateTimeOffset.UtcNow);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(jwt, new TokenValidationParameters
        {
            ValidIssuer = Reg.ClientId, ValidAudience = Reg.Issuer, IssuerSigningKey = ToolPublic, ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });
        Assert.That(result.IsValid, Is.True, result.Exception?.Message);
        var token = new JsonWebToken(jwt);
        Assert.That(token.Kid, Is.EqualTo("tool-1"));
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));
        var root = payload.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty(LtiClaims.MessageType).GetString(), Is.EqualTo(LtiClaims.DeepLinkingResponse));
            Assert.That(root.GetProperty(LtiClaims.Version).GetString(), Is.EqualTo("1.3.0"));
            Assert.That(root.GetProperty(LtiClaims.DeploymentId).GetString(), Is.EqualTo("3"));
            Assert.That(root.GetProperty(LtiClaims.DeepLinkingData).GetString(), Is.EqualTo("platform-data"));
            Assert.That(root.TryGetProperty("nonce", out _), Is.True);
            var item = root.GetProperty(LtiClaims.DeepLinkingContentItems)[0];
            Assert.That(item.GetProperty("type").GetString(), Is.EqualTo("ltiResourceLink"));
            Assert.That(item.GetProperty("custom").GetProperty("examId").GetString(), Is.EqualTo(examId.ToString()));
            Assert.That(item.GetProperty("lineItem").GetProperty("scoreMaximum").GetInt32(), Is.EqualTo(100));
        });
    }

    [Test]
    public void TheClientAssertion_NamesTheClientAndTheTokenEndpoint()
    {
        var token = new JsonWebToken(LtiTokenClient.ClientAssertion(ToolCredentials, Reg, DateTimeOffset.UtcNow));
        Assert.Multiple(() =>
        {
            Assert.That(token.Alg, Is.EqualTo("RS256"));
            Assert.That(token.Issuer, Is.EqualTo("client-abc"));
            Assert.That(token.Subject, Is.EqualTo("client-abc"));
            Assert.That(token.Audiences, Is.EquivalentTo(new[] { Reg.AuthTokenUrl }));
            Assert.That(token.Id, Is.Not.Empty);
        });
    }

    [Test]
    public void TheClientAssertion_UsesTheConfiguredAudience_ForBrightspace()
    {
        var reg = new LtiRegistration { ClientId = "c", AuthTokenUrl = "https://auth.brightspace.com/core/connect/token", TokenAudience = "https://api.brightspace.com/auth/token" };
        var token = new JsonWebToken(LtiTokenClient.ClientAssertion(ToolCredentials, reg, DateTimeOffset.UtcNow));
        Assert.That(token.Audiences, Is.EquivalentTo(new[] { "https://api.brightspace.com/auth/token" }));
    }

    [Test]
    public void TheJwk_UsesTheStandardLowercaseNames()
    {
        var json = JsonSerializer.Serialize(new JwkEntry("RSA", "k1", "sig", "RS256", "bW9k", "AQAB"));
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "kty", "kid", "use", "alg", "n", "e" }));
        Assert.That(new JsonWebKeySet("{\"keys\":[" + json + "]}").Keys, Has.Count.EqualTo(1));
    }

    [TestCase("https://canvas.example.edu/api/lti/courses/1/line_items/5", "https://canvas.example.edu/api/lti/courses/1/line_items/5/scores")]
    [TestCase("https://canvas.example.edu/api/lti/courses/1/line_items/5/", "https://canvas.example.edu/api/lti/courses/1/line_items/5/scores")]
    [TestCase("https://moodle.example.edu/mod/lti/services.php/2/lineitems/7/lineitem?type_id=1", "https://moodle.example.edu/mod/lti/services.php/2/lineitems/7/lineitem/scores?type_id=1")]
    public void TheScoresUrl_KeepsTheQueryAfterThePath(string lineItem, string expected) =>
        Assert.That(LtiGradePassbackService.ScoresUrl(lineItem), Is.EqualTo(expected));

    [Test]
    public void TheScore_IsOutOf100_CompletedAndFullyGraded()
    {
        using var doc = JsonDocument.Parse(LtiGradePassbackService.ScoreJson("user-42", 87.5m, new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero)));
        var r = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(r.GetProperty("userId").GetString(), Is.EqualTo("user-42"));
            Assert.That(r.GetProperty("scoreGiven").GetDouble(), Is.EqualTo(87.5));
            Assert.That(r.GetProperty("scoreMaximum").GetDouble(), Is.EqualTo(100));
            Assert.That(r.GetProperty("activityProgress").GetString(), Is.EqualTo("Completed"));
            Assert.That(r.GetProperty("gradingProgress").GetString(), Is.EqualTo("FullyGraded"));
            Assert.That(r.GetProperty("timestamp").GetString(), Is.EqualTo("2026-11-22T10:00:00.000Z"));
        });
    }

    [Test]
    public void TheRosterNextPage_ComesFromTheLinkHeader()
    {
        Assert.That(LtiRosterService.NextLink(["<https://lms.example.edu/nrps?page=2>; rel=\"next\", <https://lms.example.edu/nrps?page=9>; rel=\"last\""]),
            Is.EqualTo("https://lms.example.edu/nrps?page=2"));
        Assert.That(LtiRosterService.NextLink(["<https://lms.example.edu/nrps?page=9>; rel=\"last\""]), Is.Null);
        Assert.That(LtiRosterService.NextLink(["<http://insecure.example.edu/nrps?page=2>; rel=\"next\""]), Is.Null);
        Assert.That(LtiRosterService.NextLink([]), Is.Null);
    }

    [Test]
    public void ThePresets_FillEveryPlatformAddress()
    {
        foreach (var kind in Enum.GetValues<LtiPlatformKind>())
        {
            var (values, problem) = LtiPlatformGuide.Preset(kind, "https://lms.example.edu/", "client-1");
            Assert.That(problem, Is.Null, kind.ToString());
            values!.Name = "x"; values.ClientId = "client-1"; values.DeploymentId = "d";
            Assert.That(LtiAdminService.Problem(values), Is.Null, kind.ToString());
        }
        Assert.That(LtiPlatformGuide.Preset(LtiPlatformKind.Moodle, null, null).Problem, Is.Not.Null);
        Assert.That(LtiPlatformGuide.Preset(LtiPlatformKind.Moodle, "https://moodle.example.edu", null).Values!.KeySetUrl,
            Is.EqualTo("https://moodle.example.edu/mod/lti/certs.php"));
        Assert.That(LtiPlatformGuide.Preset(LtiPlatformKind.Brightspace, "https://school.brightspace.com", null).Values!.TokenAudience,
            Is.EqualTo("https://api.brightspace.com/auth/token"));
    }

    [Test]
    public void ARegistration_RefusesPlainHttpAndPrivateAddresses()
    {
        Gadfly.Server.Platform.WebhookTargets.AllowPrivate = false;
        var input = LtiPlatformGuide.Preset(LtiPlatformKind.Canvas, null, null).Values!;
        input.Name = "Canvas"; input.ClientId = "1"; input.DeploymentId = "d";
        input.KeySetUrl = "http://sso.canvaslms.com/api/lti/security/jwks";
        Assert.That(LtiAdminService.Problem(input), Does.Contain("https://"));
        input.KeySetUrl = "https://169.254.169.254/latest/meta-data";
        Assert.That(LtiAdminService.Problem(input), Does.Contain("public host"));
    }

    [Test]
    public void RandomTokens_AreLongAndAlphanumeric()
    {
        var a = LtiLaunchService.RandomToken();
        Assert.That(a, Has.Length.EqualTo(48));
        Assert.That(a, Does.Match("^[A-Za-z0-9]+$"));
        Assert.That(LtiLaunchService.RandomToken(), Is.Not.EqualTo(a));
    }
}
