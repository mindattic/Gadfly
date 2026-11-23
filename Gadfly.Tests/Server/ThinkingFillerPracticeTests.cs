using Gadfly.Server.Components.Pages.Student;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>GAD-T627: the practice interview never gets the typing arm of the wait A/B test.</summary>
[TestFixture]
public class ThinkingFillerPracticeTests
{
    [Test]
    public void Practice_never_picks_the_typing_arm_even_at_full_share()
    {
        for (var i = 0; i < 500; i++)
        {
            var id = Guid.NewGuid();
            Assert.That(ThinkingFiller.ArmFor(id, 1.0, abTest: false), Is.EqualTo(ThinkingFiller.Gesture));
        }
    }

    [Test]
    public void An_exam_still_gets_the_typing_arm_at_full_share()
        => Assert.That(ThinkingFiller.ArmFor(Guid.NewGuid(), 1.0), Is.EqualTo(ThinkingFiller.Typing));

    [Test]
    public void PracticePage_turns_the_wait_ab_test_off()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Gadfly.Server", "Components", "Pages", "Student", "PracticePage.razor")))
            root = Path.GetDirectoryName(root);
        Assert.That(root, Is.Not.Null);
        var page = File.ReadAllText(Path.Combine(root!, "Gadfly.Server", "Components", "Pages", "Student", "PracticePage.razor"));
        Assert.That(page, Does.Contain("AllowWaitAbTest=\"false\""));
    }
}
