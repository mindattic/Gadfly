using System.Text.RegularExpressions;
using Gadfly.Server.Admin;
using Gadfly.Server.Components.Pages.Student;
using Gadfly.Server.Localization;
using Gadfly.Server.Research;
using Gadfly.Server.Review;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The exam room in the attempt's language (GAD-T606): every exam-room key exists in every UI language, every key the exam room's
/// scripts ask for exists with the same slots, the examiner's pronouns follow the examiner, staff actions reach the room language-
/// neutral, and the student's research notice, report and integrity notes read in their language while English stays word for word.
/// </summary>
[TestFixture]
public class ExamRoomLocalizationTests
{
    private static readonly UiCatalog Catalog = new();

    private static IEnumerable<string> ExamRoomKeys(string language) =>
        Catalog.Table(language).Keys.Where(k => k.StartsWith("examRoom.", StringComparison.Ordinal));

    [Test]
    public void EveryExamRoomKey_ExistsInEveryLanguage()
    {
        Assert.That(ExamRoomKeys("en").Count(), Is.GreaterThan(200));
        foreach (var lang in UiCatalog.Languages.Where(l => l != UiCatalog.English))
        {
            Assert.That(ExamRoomKeys("en").Except(ExamRoomKeys(lang)), Is.Empty, $"untranslated in {lang}");
            Assert.That(ExamRoomKeys(lang).Except(ExamRoomKeys("en")), Is.Empty, $"orphaned in {lang}");
        }
    }

    [Test]
    public void EveryKeyTheExamRoomScriptsAskFor_ExistsInEnglish_WithTheFallbacksSlots()
    {
        var server = RepoFiles.ServerFolder();
        if (server is null) { Assert.Ignore("Gadfly.Server sources not found"); return; }
        var call = new Regex(@"\bt\(""(examRoom\.[a-zA-Z0-9_.]+)""(?:,\s*""((?:[^""\\]|\\.)*)"")?");
        var slots = new Regex(@"\{(\w+)\}");
        string Slots(string s) => string.Join(",", slots.Matches(s).Select(m => m.Groups[1].Value).Distinct().Order());
        var problems = new List<string>();
        var found = 0;
        foreach (var file in new[] { "voice-room.js", "examiner-gaze.js" })
        {
            var text = File.ReadAllText(Path.Combine(server, "wwwroot", "js", file));
            foreach (Match m in call.Matches(text))
            {
                found++;
                var key = m.Groups[1].Value;
                if (Catalog.Find("en", key) is not { } en) { problems.Add($"{file}: {key} is not defined"); continue; }
                if (m.Groups[2].Success && Slots(en) != Slots(m.Groups[2].Value)) problems.Add($"{file}: {key} slots differ from its fallback");
            }
        }
        Assert.That(found, Is.GreaterThan(25));
        Assert.That(problems, Is.Empty);
        // examiner-gaze.js builds its keys from the state name: every one of them exists.
        foreach (var state in new[] { "loading", "calibrating", "calibratingSimple", "setup", "on", "noFace", "twoFaces", "lost", "unknown", "accommodated", "unavailable", "setupCaption", "setupRetry", "skip" })
            Assert.That(Catalog.Find("en", "examRoom.js.gaze." + state), Is.Not.Null, state);
    }

    /// <summary>
    /// GAD-T819: the student's live transcript shows only who said what, so the response-delay and voice-comparison strings are gone
    /// from every UI language.
    /// </summary>
    [Test]
    public void DeliveryMeasureStrings_AreGone()
    {
        string[] keys = ["examRoom.transcript.delay", "examRoom.voiceLine.baseline", "examRoom.voiceLine.close", "examRoom.voiceLine.against",
            "examRoom.voiceLine.feature.pitch", "examRoom.voiceLine.feature.pitchVariability", "examRoom.voiceLine.feature.jitter",
            "examRoom.voiceLine.feature.loudness", "examRoom.voiceLine.feature.rate", "examRoom.voiceLine.feature.pauses"];
        foreach (var lang in UiCatalog.Languages)
            foreach (var key in keys)
                Assert.That(Catalog.Find(lang, key), Is.Null, $"{lang}: {key}");
    }

    [Test]
    public void ScriptSlots_AreKeptInEveryTranslation()
    {
        var slots = new Regex(@"\{(\w+)\}");
        string Slots(string s) => string.Join(",", slots.Matches(s).Select(m => m.Groups[1].Value).Distinct().Order());
        foreach (var lang in UiCatalog.Languages.Where(l => l != UiCatalog.English))
        {
            var bad = Catalog.Table(lang).Where(kv => kv.Key.StartsWith("examRoom.js.", StringComparison.Ordinal)
                && Catalog.Find("en", kv.Key) is { } en && Slots(en) != Slots(kv.Value)).Select(kv => kv.Key);
            Assert.That(bad, Is.Empty, lang);
        }
    }

    [Test]
    public void ThePronouns_FollowTheExaminer_AndEveryMaleVariantHasItsSentence()
    {
        var male = Catalog.Keys.Where(k => k.EndsWith(ExamRoomText.MaleSuffix, StringComparison.Ordinal)).ToList();
        Assert.That(male, Is.Not.Empty);
        foreach (var key in male)
        {
            var baseKey = key[..^ExamRoomText.MaleSuffix.Length];
            Assert.That(Catalog.Find("en", baseKey), Is.Not.Null, baseKey);
            Assert.That(Regex.IsMatch(Catalog.Find("en", key)!, @"\b(she|her|hers|herself)\b", RegexOptions.IgnoreCase), Is.False, key);
        }
        var en = new UiText(Catalog, "en");
        Assert.That(new ExamRoomText(en)["examRoom.gaze.note"], Does.Contain("the screen around her"));
        Assert.That(new ExamRoomText(en, maleExaminer: true)["examRoom.gaze.note"], Does.Contain("the screen around him").And.Contain("he may say"));
        // A sentence with no pronoun has no variant: the same text for either examiner.
        Assert.That(new ExamRoomText(en, maleExaminer: true)["examRoom.voice.done"], Is.EqualTo("Done answering"));
        // Korean names the examiner 시험관: the same sentence for either.
        var ko = new UiText(Catalog, "ko");
        Assert.That(new ExamRoomText(ko, maleExaminer: true)["examRoom.gaze.note"], Is.EqualTo(new ExamRoomText(ko)["examRoom.gaze.note"]));
    }

    [Test]
    public void TheRoomText_IsInTheAttemptsLanguage_WhateverTheSiteLanguage()
    {
        var site = new UiText(Catalog, "en");
        Assert.That(new ExamRoomText(site.For("ko"))["examRoom.voice.done"], Is.EqualTo("답변 완료"));
        Assert.That(new ExamRoomText(site.For("ko"))["examRoom.progress", 2, 5], Is.EqualTo("완료한 주제 2/5"));
        Assert.That(new ExamRoomText(site)["examRoom.progress", 2, 5], Is.EqualTo("Topics finished 2 of 5"));
    }

    [Test]
    public void StaffActions_ReachTheRoomLanguageNeutral_AndReadAsBeforeInEnglish()
    {
        var en = new UiText(Catalog, "en");
        var ko = new UiText(Catalog, "ko");
        var extended = StaffActionMessage.Encode(StaffActionMessage.Extended, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(23.4));
        Assert.That(StaffActionMessage.Render(extended, en), Is.EqualTo("Your instructor gave you 5 minutes more. You now have 23 minutes left."));
        Assert.That(StaffActionMessage.Render(extended, ko), Is.EqualTo("담당 교수자가 시간을 5분 더 주었어요. 이제 23분 남았어요."));
        Assert.That(StaffActionMessage.Render(StaffActionMessage.Encode(StaffActionMessage.Resumed, TimeSpan.FromSeconds(30)), en),
            Is.EqualTo("Your instructor resumed your exam. You have less than a minute left."));
        Assert.That(StaffActionMessage.Render(StaffActionMessage.Encode(StaffActionMessage.Ended), en),
            Is.EqualTo("Your instructor ended your exam. Your answers so far were submitted."));
        Assert.That(StaffActionMessage.Render("Plain words from an older server.", ko), Is.EqualTo("Plain words from an older server."));
    }

    [Test]
    public void TheResearchNotice_IsInTheStudentsLanguage_AndEnglishIsUnchanged()
    {
        var legacy = ResearchConsentNotice.Invitation("Org", "Pilot", "contact@x", "IRB-1", 90);
        var viaCatalog = ResearchConsentNotice.Invitation("Org", "Pilot", "contact@x", "IRB-1", 90, new UiText(Catalog, "en"));
        Assert.That(viaCatalog.Sha256, Is.EqualTo(legacy.Sha256), "English is built exactly as before");
        // The English string file says the same, paragraph for paragraph (the translators' source).
        var en = new UiText(Catalog, "en");
        Assert.That(legacy.Title, Is.EqualTo(en["research.invitation.title"].Value));
        Assert.That(legacy.Paragraphs[0], Is.EqualTo(en["research.invitation.p1", "Org", "Pilot"].Value));
        Assert.That(legacy.Paragraphs[4], Is.EqualTo(en["research.invitation.p5", 90].Value));
        Assert.That(ResearchConsentNotice.Withdrawal("Org").Paragraphs[0], Is.EqualTo(en["research.withdrawal.p1", "Org"].Value));

        var ko = ResearchConsentNotice.Invitation("Org", "Pilot", "contact@x", "IRB-1", 90, new UiText(Catalog, "ko"));
        Assert.That(ko.Paragraphs, Has.Count.EqualTo(legacy.Paragraphs.Count));
        Assert.That(ko.Sha256, Is.Not.EqualTo(legacy.Sha256), "the record says which language was read");
        Assert.That(string.Join("", ko.Paragraphs), Does.Contain("90일").And.Contain("Org"));
        Assert.That(ResearchConsentNotice.Withdrawal("Org", new UiText(Catalog, "ko")).Title, Is.EqualTo("연구 참여 철회"));
    }

    [Test]
    public void IntegrityNoteText_InEnglish_IsTheReviewersWording()
    {
        var types = Catalog.Table("en").Keys.Where(k => k.StartsWith("integrityNote.", StringComparison.Ordinal) && k.EndsWith(".label", StringComparison.Ordinal))
            .Select(k => k["integrityNote.".Length..^".label".Length]).ToList();
        Assert.That(types, Has.Count.GreaterThan(25));
        foreach (var type in types)
        {
            var (label, explanation, _) = IntegrityExplanations.For(type, "focus");
            Assert.That(Catalog.Find("en", $"integrityNote.{type}.label"), Is.EqualTo(label), type);
            Assert.That(Catalog.Find("en", $"integrityNote.{type}.explanation"), Is.EqualTo(explanation), type);
        }
    }

    [Test]
    public void StudentIntegrityNotes_ReadInTheStudentsLanguage()
    {
        var start = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
        var e = new Gadfly.Server.Data.IntegrityEvent
        {
            Type = "tab-hidden", Source = "browser", Summary = "Tab hidden for 12 s.", Severity = Gadfly.Server.Data.IntegritySeverity.Low,
            StartedAt = start.AddSeconds(245), EndedAt = start.AddSeconds(257),
        };
        var ko = new UiText(Catalog, "ko");
        Assert.That(Gadfly.Server.Integrity.StudentIntegrityRecord.When(e, start, ko), Is.EqualTo("시험 시작 4분 05초 후, 12초 동안"));
        Assert.That(Gadfly.Server.Integrity.StudentIntegrityRecord.When(e, start), Is.EqualTo("4 min 05 s into the exam, for 12 s"));
        var view = Gadfly.Server.Integrity.StudentIntegrityRecord.Build([e], new Dictionary<Guid, int>(), start, ko);
        Assert.That(view.Notes.Single().Label, Is.EqualTo("시험 탭이 가려짐"));
        var english = Gadfly.Server.Integrity.StudentIntegrityRecord.Build([e], new Dictionary<Guid, int>(), start, new UiText(Catalog, "en"));
        Assert.That(english.Notes.Single().Label, Is.EqualTo(IntegrityExplanations.For("tab-hidden", "browser").Label));
    }
}
