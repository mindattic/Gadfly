using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// Every credential comes from MindAttic.Vault (GAD-T803), never a literal in checked-in configuration
/// (appsettings*.json, launchSettings.json).
/// </summary>
[TestFixture]
public class CredentialSourcesTests
{
    private static readonly Regex CredentialName = new(@"(pepper|token|apikey|api_key|password|passwd|pwd|secret)", RegexOptions.IgnoreCase);

    private static string RootOrIgnore() =>
        RepoFiles.Root() ?? throw new IgnoreException("The repository is not next to the test binaries.");

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
        Assert.That(found, Is.Empty, "Credentials belong in MindAttic.Vault, not in checked-in configuration.");
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
