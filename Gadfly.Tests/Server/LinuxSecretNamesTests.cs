using Microsoft.Extensions.Configuration;
using MindAttic.Authentication.Secrets;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Linux App Service refuses app-setting names with a dash (GAD-T781), so the (since removed, GAD-T804) Azure provisioning set the auth secrets as
/// MindAttic__Vault__Security__pepper_v1 / reset_token_key / bootstrap_token. The MindAttic.Authentication version Gadfly
/// ships must resolve those to the very same values the dashed Windows names gave, or every stored password stops verifying.
/// </summary>
[TestFixture]
public class LinuxSecretNamesTests
{
    private static ConfigAuthSecrets Secrets(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>($"{ConfigAuthSecrets.SectionPath}:{s.Key}", s.Value)))
            .Build());

    [TestCase("pepper.v1", "pepper_v1")]
    [TestCase("pepper.v1", "pepper-v1")]
    [TestCase("reset-token-key", "reset_token_key")]
    [TestCase("reset-token-key", "reset-token-key")]
    [TestCase("bootstrap-token", "bootstrap_token")]
    [TestCase("bootstrap-token", "bootstrap-token")]
    public void Underscore_and_dashed_setting_names_resolve_to_the_same_value(string requested, string settingName)
    {
        const string value = "c2FtZS1ieXRlcy1laXRoZXItd2F5LXRvLXRoZS1wZXBwZXI=";
        Assert.That(Secrets((settingName, value)).GetRequired(requested), Is.EqualTo(value));
    }

    [Test]
    public void Both_spellings_side_by_side_with_one_value_still_resolve()
    {
        // A Windows site re-provisioned with the Linux-safe names carries both, both pointing at one Key Vault secret.
        const string value = "c2FtZS1ieXRlcw==";
        var secrets = Secrets(("pepper-v1", value), ("pepper_v1", value));
        Assert.That(secrets.GetRequiredBytes("pepper.v1"), Is.EqualTo(Convert.FromBase64String(value)));
    }
}
