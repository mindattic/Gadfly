namespace Gadfly.Tests.Fakes;

/// <summary>A clock tests move by hand (engine sessions, server time limits, usage months).</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}
