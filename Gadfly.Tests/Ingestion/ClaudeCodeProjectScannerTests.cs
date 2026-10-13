using Gadfly.Ingestion;
using NUnit.Framework;

namespace Gadfly.Tests.Ingestion;

[TestFixture]
public class ClaudeCodeProjectScannerTests
{
    private string root = "";

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "gadfly-scanner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Test]
    public void ScanAll_SkipsMemoryOnlyFolders()
    {
        var memoryOnly = Path.Combine(root, "D--Projects-MindAttic-SomeProject");
        Directory.CreateDirectory(Path.Combine(memoryOnly, "memory"));
        File.WriteAllText(Path.Combine(memoryOnly, "memory", "MEMORY.md"), "# notes");

        var withTranscript = Path.Combine(root, "D--Projects-MindAttic-OtherProject");
        Directory.CreateDirectory(withTranscript);
        File.WriteAllText(Path.Combine(withTranscript, "session1.jsonl"), "{}");

        var results = ClaudeCodeProjectScanner.ScanAll(root).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ProjectFolder, Is.EqualTo("D--Projects-MindAttic-OtherProject"));
    }

    [Test]
    public void ScanAll_ReturnsEmptyWhenRootDoesNotExist()
    {
        Assert.That(ClaudeCodeProjectScanner.ScanAll(Path.Combine(root, "does-not-exist")).ToList(), Is.Empty);
    }

    [TestCase(@"D:\Projects\MindAttic\Gadfly", "D--Projects-MindAttic-Gadfly")]
    [TestCase(@"D:\Projects\MindAttic\MindAttic.Psst", "D--Projects-MindAttic-MindAttic-Psst")]
    public void FolderNameFor_MatchesObservedClaudeCodeConvention(string projectPath, string expected)
    {
        Assert.That(ClaudeCodeProjectScanner.FolderNameFor(projectPath), Is.EqualTo(expected));
    }
}
