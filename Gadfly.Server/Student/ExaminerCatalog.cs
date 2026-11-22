using Gadfly.Server.Voice;

namespace Gadfly.Server.Student;

/// <summary>One examiner a student can choose on the get-ready page (GAD-T595).</summary>
/// <param name="Id">Stable id, also what an exam's AvatarId names.</param>
/// <param name="NameKey">UI string key of the examiner's description ("getReady.examiner.choice.female").</param>
/// <param name="ModelUrl">The 3D model, or null for the portrait only.</param>
/// <param name="PortraitUrl">The 2D portrait (always shown first, and on devices without 3D).</param>
/// <param name="Available">False while the examiner's model or portrait isn't installed: listed as coming soon, not choosable.</param>
public sealed record ExaminerOption(string Id, string NameKey, string? ModelUrl, string PortraitUrl, bool Available);

/// <summary>
/// The examiners Gadfly offers (GAD-T595): the current female examiner, and the male counterpart (GAD-T504) once its model and
/// portrait are installed under wwwroot/assets. A list rather than one hard-coded examiner, so adding one is a row here.
/// </summary>
public static class ExaminerCatalog
{
    public const string Female = "examiner-f";
    public const string Male = "examiner-m";

    /// <summary>Where the male examiner's files go when GAD-T504 delivers them (relative to wwwroot).</summary>
    public const string MaleModelPath = "assets/avatars/examiner-m.glb";
    public const string MalePortraitPath = "assets/portrait-m.png";

    /// <param name="voice">The site's voice settings: the female examiner is the configured one.</param>
    /// <param name="webRoot">wwwroot on disk, to see whether an examiner's files are installed (null: only the configured one).</param>
    public static IReadOnlyList<ExaminerOption> For(VoiceSettings voice, string? webRoot)
    {
        var maleReady = webRoot is not null && File.Exists(Path.Combine(webRoot, MalePortraitPath));
        var maleModel = webRoot is not null && File.Exists(Path.Combine(webRoot, MaleModelPath)) ? "/" + MaleModelPath : null;
        return
        [
            new(Female, "getReady.examiner.choice.female", voice.AvatarModelUrl, voice.PortraitUrl, true),
            new(Male, "getReady.examiner.choice.male", maleModel, "/" + MalePortraitPath, maleReady),
        ];
    }

    /// <summary>The examiner to show first: the remembered one if it is still offered, else the first available.</summary>
    public static ExaminerOption Pick(IReadOnlyList<ExaminerOption> options, string? rememberedId) =>
        options.FirstOrDefault(o => o.Available && o.Id == rememberedId) ?? options.First(o => o.Available);
}
