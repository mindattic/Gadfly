using System.IO.Compression;
using Gadfly.Cli;
using Gadfly.Socrates;
using MindAttic.Legion;
using NUnit.Framework;

namespace Gadfly.Tests.Cli;

/// <summary>
/// The Gadfly CLI's argument handling and exit codes (GAD-T6). Only paths that return before any
/// provider client is built are driven end to end; examine/interrogate flag parsing is tested through the
/// extracted options records, so no test can reach a live panel or the user's corpus.
/// </summary>
[TestFixture]
[NonParallelizable] // swaps Console.Out / Console.Error
public class GadflyCliTests
{
    private TextWriter originalOut = null!;
    private TextWriter originalErr = null!;
    private StringWriter stdout = null!;
    private StringWriter stderr = null!;
    private string dir = "";

    [SetUp]
    public void SetUp()
    {
        originalOut = Console.Out;
        originalErr = Console.Error;
        stdout = new StringWriter();
        stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        dir = Path.Combine(Path.GetTempPath(), "gadfly-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetOut(originalOut);
        Console.SetError(originalErr);
        stdout.Dispose();
        stderr.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private string MakeZip(string name, params (string EntryName, string Content)[] entries)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
            writer.Write(content);
        }
        return path;
    }

    private const string TwoMessageExport = """
        [ { "uuid": "c1", "chat_messages": [
            { "sender": "human", "text": "hello", "created_at": "2026-01-01T00:00:01Z" },
            { "sender": "assistant", "text": "hi", "created_at": "2026-01-01T00:00:02Z" } ] } ]
        """;

    /// <summary>Snapshot of the user's real corpus directory, to prove a dry run does not write to it.</summary>
    private static string CorpusSnapshot()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "Gadfly", "corpus");
        if (!Directory.Exists(root)) return "(absent)";
        return string.Join("|", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f)
            .Select(f => $"{f}:{new FileInfo(f).Length}:{File.GetLastWriteTimeUtc(f).Ticks}"));
    }

    // ---- GadflyCli top level -----------------------------------------------------------------

    [Test]
    public async Task NoArguments_PrintsUsage_AndReturns1()
    {
        var code = await GadflyCli.RunAsync([]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(stdout.ToString(), Does.Contain("gadfly <command>"));
    }

    [TestCase("--help")]
    [TestCase("-h")]
    [TestCase("help")]
    [TestCase("/?")]
    public async Task HelpFlags_PrintUsage_AndReturn0(string flag)
    {
        var code = await GadflyCli.RunAsync([flag]);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(stdout.ToString(), Does.Contain("Commands:"));
    }

    [Test]
    public async Task UnknownCommand_ReturnsA1_AndNamesTheCommand()
    {
        var code = await GadflyCli.RunAsync(["frobnicate"]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(stderr.ToString(), Does.Contain("unknown command 'frobnicate'"));
    }

    [Test]
    public async Task Version_ReturnsZero_AndPrintsTheVersion()
    {
        var code = await GadflyCli.RunAsync(["version"]);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(stdout.ToString(), Does.StartWith("gadfly "));
    }

    [Test]
    public async Task CommandNames_AreCaseInsensitive()
    {
        var code = await GadflyCli.RunAsync(["VERSION"]);

        Assert.That(code, Is.EqualTo(0));
    }

    [Test]
    public async Task ImportIsRoutedFromTheTopLevel()
    {
        var code = await GadflyCli.RunAsync(["import"]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(stdout.ToString(), Does.Contain("gadfly import <zip>"));
    }

    [TestCase("examine")]
    [TestCase("interrogate")]
    public async Task ExamineAndInterrogate_HelpReturns0_WithoutBuildingAClient(string command)
    {
        var code = await GadflyCli.RunAsync([command, "--help"]);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(stdout.ToString(), Does.Contain($"gadfly {command} [opts]"));
    }

    [Test]
    public async Task Examine_RejectsUntrustedVoters_WithExit2_BeforeBuildingAClient()
    {
        var code = await GadflyCli.RunAsync(["examine", "--voters", "claude,mistral,gemini"]);

        Assert.That(code, Is.EqualTo(2));
        Assert.That(stderr.ToString(), Does.Contain("untrusted provider id(s) mistral"));
    }

    [Test]
    public async Task Interrogate_RejectsUntrustedVoters_WithExit2_BeforeBuildingAClient()
    {
        var code = await GadflyCli.RunAsync(["interrogate", "--single-voter", "mistral"]);

        Assert.That(code, Is.EqualTo(2));
        Assert.That(stderr.ToString(), Does.Contain("untrusted provider id(s) mistral"));
    }

    // ---- import ------------------------------------------------------------------------------

    [Test]
    public async Task Import_WithNoArguments_Returns1()
    {
        Assert.That(await ImportCommand.RunAsync([]), Is.EqualTo(1));
    }

    [TestCase("--help")]
    [TestCase("-h")]
    public async Task Import_Help_Returns0(string flag)
    {
        Assert.That(await ImportCommand.RunAsync([flag]), Is.EqualTo(0));
        Assert.That(stdout.ToString(), Does.Contain("--dry-run"));
    }

    [Test]
    public async Task Import_FlagsOnly_ReportsAMissingZip_AndReturns1()
    {
        var code = await ImportCommand.RunAsync(["--dry-run"]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(stderr.ToString(), Does.Contain("missing <zip> path"));
    }

    [Test]
    public async Task Import_MissingZipFile_Returns1()
    {
        var code = await ImportCommand.RunAsync([Path.Combine(dir, "does-not-exist.zip")]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(stderr.ToString(), Does.Contain("not found"));
    }

    [Test]
    public async Task Import_FileThatIsNotAZip_Returns1()
    {
        var path = Path.Combine(dir, "fake.zip");
        File.WriteAllText(path, "definitely not a zip archive");

        var code = await ImportCommand.RunAsync(["--dry-run", path]);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(stderr.ToString(), Does.Contain("not a readable zip"));
    }

    [Test]
    public async Task Import_ZipWithoutConversationsJson_IsASchemaMismatch_Returns2()
    {
        var zip = MakeZip("export.zip", ("users.json", "[]"));

        var code = await ImportCommand.RunAsync(["--dry-run", zip]);

        Assert.That(code, Is.EqualTo(2));
        Assert.That(stderr.ToString(), Does.Contain("schema mismatch"));
    }

    [Test]
    public async Task Import_ConversationsJsonOfTheWrongShape_IsASchemaMismatch_Returns2()
    {
        var zip = MakeZip("export.zip", ("conversations.json", """{ "not": "an array" }"""));

        var code = await ImportCommand.RunAsync(["--dry-run", zip]);

        Assert.That(code, Is.EqualTo(2));
    }

    [Test]
    public async Task Import_DryRun_ReportsTheMessageCount_AndWritesNothing()
    {
        var zip = MakeZip("export.zip", ("conversations.json", TwoMessageExport));
        var before = CorpusSnapshot();

        var code = await ImportCommand.RunAsync(["--dry-run", zip]);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(stdout.ToString(), Does.Contain("parsed 2 messages").And.Contain("nothing written"));
        Assert.That(CorpusSnapshot(), Is.EqualTo(before), "a dry run must not touch the corpus");
    }

    [Test]
    public async Task Import_DryRun_FlagMayFollowTheZipPath()
    {
        var zip = MakeZip("export.zip", ("conversations.json", TwoMessageExport));

        var code = await ImportCommand.RunAsync([zip, "--DRY-RUN"]);

        Assert.That(code, Is.EqualTo(0));
        Assert.That(stdout.ToString(), Does.Contain("nothing written"));
    }

    // ---- examine options ---------------------------------------------------------------------

    [Test]
    public void Examine_Defaults()
    {
        var o = ExamineCommand.ExamineOptions.Parse([]);

        Assert.That(o.Tier, Is.EqualTo(LegionTierResolver.DefaultTier));
        Assert.That(o.Providers, Is.Null);
        Assert.That(o.OutPath, Is.Null);
    }

    [Test]
    public void Examine_ParsesTierVotersAndOut()
    {
        var o = ExamineCommand.ExamineOptions.Parse(["--tier", "LOW", "--voters", "deepseek, gemini ,openai", "--out", "x.txt"]);

        Assert.That(o.Tier, Is.EqualTo(ModelTier.Low));
        Assert.That(o.Providers, Is.EqualTo(new[] { "deepseek", "gemini", "openai" }));
        Assert.That(o.OutPath, Is.EqualTo("x.txt"));
    }

    [TestCase("claude")]
    [TestCase("claude,openai")]
    [TestCase("claude,openai,gemini,deepseek")]
    [TestCase(",,")]
    public void Examine_VotersWithOtherThanThreeIds_WarnsAndKeepsDefaults(string voters)
    {
        var o = ExamineCommand.ExamineOptions.Parse(["--voters", voters]);

        Assert.That(o.Providers, Is.Null);
        Assert.That(stderr.ToString(), Does.Contain("needs exactly 3 provider ids"));
    }

    [Test]
    public void Examine_InvalidTier_KeepsTheDefault()
    {
        var o = ExamineCommand.ExamineOptions.Parse(["--tier", "ludicrous"]);

        Assert.That(o.Tier, Is.EqualTo(LegionTierResolver.DefaultTier));
    }

    [Test]
    public void Examine_AFlagWithNoValue_IsIgnored()
    {
        var o = ExamineCommand.ExamineOptions.Parse(["--tier"]);

        Assert.That(o.Tier, Is.EqualTo(LegionTierResolver.DefaultTier));
    }

    // ---- interrogate options -----------------------------------------------------------------

    [Test]
    public void Interrogate_Defaults()
    {
        var o = InterrogateCommand.InterrogateOptions.Parse([]);

        Assert.That(o.Tier, Is.EqualTo(LegionTierResolver.DefaultTier));
        Assert.That(o.MaxTurns, Is.EqualTo(20));
        Assert.That(o.Quorum, Is.EqualTo(Quorum.Plurality));
        Assert.That(o.Providers, Is.Null);
    }

    [Test]
    public void Interrogate_ParsesTierMaxTurnsQuorumAndVoters()
    {
        var o = InterrogateCommand.InterrogateOptions.Parse(
            ["--tier", "highest", "--max-turns", "5", "--quorum", "Unanimous", "--voters", "claude,openai"]);

        Assert.That(o.Tier, Is.EqualTo(ModelTier.Highest));
        Assert.That(o.MaxTurns, Is.EqualTo(5));
        Assert.That(o.Quorum, Is.EqualTo(Quorum.Unanimous));
        Assert.That(o.Providers, Is.EqualTo(new[] { "claude", "openai" }));
    }

    [TestCase("0")]
    [TestCase("-3")]
    [TestCase("many")]
    [TestCase("")]
    public void Interrogate_InvalidMaxTurns_KeepsTheDefault(string value)
    {
        var o = InterrogateCommand.InterrogateOptions.Parse(["--max-turns", value]);

        Assert.That(o.MaxTurns, Is.EqualTo(20));
    }

    [Test]
    public void Interrogate_InvalidTier_KeepsTheDefault()
    {
        var o = InterrogateCommand.InterrogateOptions.Parse(["--tier", "ludicrous"]);

        Assert.That(o.Tier, Is.EqualTo(LegionTierResolver.DefaultTier));
    }

    [Test]
    public void Interrogate_InvalidQuorum_KeepsTheDefault()
    {
        var o = InterrogateCommand.InterrogateOptions.Parse(["--quorum", "whoever-shouts-loudest"]);

        Assert.That(o.Quorum, Is.EqualTo(Quorum.Plurality));
    }

    [Test]
    public void Interrogate_SingleVoter_NarrowsToOneProvider()
    {
        var o = InterrogateCommand.InterrogateOptions.Parse(["--single-voter", "gemini"]);

        Assert.That(o.Providers, Is.EqualTo(new[] { "gemini" }));
    }

    [Test]
    public void Interrogate_TheLastVoterFlagWins()
    {
        var o = InterrogateCommand.InterrogateOptions.Parse(["--voters", "claude,openai", "--single-voter", "deepseek"]);

        Assert.That(o.Providers, Is.EqualTo(new[] { "deepseek" }));
    }

    [Test]
    public void Interrogate_VotersAreParsedVerbatim_SoUntrustedIdsAreCaughtByTheTrustCheck()
    {
        var o = InterrogateCommand.InterrogateOptions.Parse(["--voters", "claude,mistral"]);

        Assert.That(LegionTierResolver.UntrustedIds(o.Providers), Is.EqualTo(new[] { "mistral" }));
    }
}
