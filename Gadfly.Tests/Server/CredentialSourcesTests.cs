using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Every credential comes from Vault (GAD-T803): MindAttic.Vault's files on a dev machine, Key Vault references in Azure.
/// provision-azure.ps1 is the source of the App Service settings, so each credential setting it writes must be a Key Vault
/// reference and the database connection must sign in with Entra ID, not a password. The checked-in configuration
/// (appsettings*.json, launchSettings.json) carries no credential at all.
/// </summary>
[TestFixture]
public class CredentialSourcesTests
{
    private static readonly Regex CredentialName = new(@"(pepper|token|apikey|api_key|password|passwd|pwd|secret)", RegexOptions.IgnoreCase);
    private static readonly Regex SettingLine = new(@"^\s*""(?<name>[A-Za-z0-9_.]+)=(?<value>.*)"",?\s*$", RegexOptions.Multiline);
    private static readonly Regex KeyVaultReference = new(@"^\$\(& \$kvRef '[a-z0-9-]+'\)$");

    private static string RootOrIgnore() =>
        RepoFiles.Root() ?? throw new IgnoreException("The repository is not next to the test binaries.");

    private static Dictionary<string, string> ProvisionedSettings()
    {
        var script = File.ReadAllText(Path.Combine(RootOrIgnore(), "tools", "provision-azure.ps1"));
        return SettingLine.Matches(script).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
    }

    [Test]
    public void Every_credential_setting_provisioned_in_Azure_is_a_Key_Vault_reference()
    {
        var credentials = ProvisionedSettings().Where(s => CredentialName.IsMatch(s.Key)).ToList();
        Assert.That(credentials.Select(c => c.Key), Does.Contain("MindAttic__Vault__Security__pepper_v1"),
            "provision-azure.ps1's settings were not found; the parser no longer matches the script.");
        Assert.Multiple(() =>
        {
            foreach (var (name, value) in credentials)
                Assert.That(KeyVaultReference.IsMatch(value), Is.True, $"{name} must be a Key Vault reference ($kvRef), not a literal.");
        });
    }

    [Test]
    public void The_provisioned_connection_string_signs_in_with_Entra_ID_and_holds_no_password()
    {
        var connection = ProvisionedSettings()["ConnectionStrings__Gadfly"];
        Assert.That(connection, Does.Contain("Authentication=Active Directory Default"));
        Assert.That(connection, Does.Not.Match(@"(?i)(password|pwd|user id|uid)\s*="));
    }

    [Test]
    public void Checked_in_configuration_holds_no_credentials()
    {
        var root = RootOrIgnore();
        var files = new[] { "Gadfly.Server", "Gadfly.Worker" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.json", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) is var n && (n.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                                                         || n.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Assert.That(files, Is.Not.Empty);

        var found = new List<string>();
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            Walk(doc.RootElement, Path.GetRelativePath(root, file), found);
        }
        Assert.That(found, Is.Empty, "Credentials belong in MindAttic.Vault (locally) or Key Vault (Azure), not in checked-in configuration.");
    }

    private static void Walk(JsonElement element, string path, List<string> found)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) Walk(item, path, found);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            var name = $"{path}:{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.String && CredentialName.IsMatch(property.Name)
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                found.Add(name);
            Walk(property.Value, name, found);
        }
    }
}
