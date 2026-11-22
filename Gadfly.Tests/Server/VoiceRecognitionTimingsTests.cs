using Gadfly.Server.Diagnostics;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>Recognizer latency and unsure-word rate counters, in memory only (GAD-T361). Pure: no database, no provider.</summary>
[TestFixture]
public class VoiceRecognitionTimingsTests
{
    [Test]
    public void No_samples_reports_plainly_rather_than_dividing_by_zero()
    {
        var report = new VoiceRecognitionTimingRecorder().Report();
        Assert.That(report, Does.Contain("no samples yet"));
    }

    [Test]
    public void Latency_and_unsure_share_are_counted_and_summarized()
    {
        var recorder = new VoiceRecognitionTimingRecorder();
        recorder.Record([100, 200, 300], 0.5, 10);
        recorder.Record([400], 0.1, 20);

        var report = recorder.Report();
        Assert.That(report, Does.Contain("count 4"), "three latencies from the first call, one from the second");
        Assert.That(report, Does.Contain("count 2"), "one unsure-share sample per Record call");
    }

    [Test]
    public void Nonfinite_or_out_of_range_values_are_dropped_rather_than_corrupting_the_average()
    {
        var recorder = new VoiceRecognitionTimingRecorder();
        recorder.Record([double.NaN, double.PositiveInfinity, -5, 50], 1.5, 10);
        recorder.Record(null, null, 0);   // nothing to record; must not throw
        var report = recorder.Report();
        Assert.That(report, Does.Contain("count 1"), "only the one finite, in-range latency (50) is kept");
    }

    [Test]
    public void Samples_beyond_the_cap_drop_the_oldest()
    {
        var recorder = new VoiceRecognitionTimingRecorder();
        for (var i = 0; i < VoiceRecognitionTimingRecorder.MaxSamples + 50; i++) recorder.Record([i], null, 0);
        var report = recorder.Report();
        Assert.That(report, Does.Contain($"count {VoiceRecognitionTimingRecorder.MaxSamples}"));
    }
}
