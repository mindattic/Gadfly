using Gadfly.Server.Admin;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The students import file (GAD-T296): the CSV reader and the per-row checks, before any database is involved.</summary>
[TestFixture]
public class StudentCsvTests
{
    [Test]
    public void Reader_HandlesQuotes_EmbeddedCommasAndLineBreaks_Bom_AndBlankLines()
    {
        var rows = CsvReader.Read("﻿email,name\r\n\"a@x.edu\",\"Lovelace, Ada\"\r\n\r\nb@x.edu,\"Two\nLines\"\nc@x.edu,\"Say \"\"hi\"\"\"");
        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[0].Cells, Is.EqualTo(new[] { "email", "name" }));
        Assert.That(rows[1].Cells, Is.EqualTo(new[] { "a@x.edu", "Lovelace, Ada" }));
        Assert.That(rows[2].Line, Is.EqualTo(4), "the blank line 3 is skipped but still counted");
        Assert.That(rows[2].Cells[1], Is.EqualTo("Two\nLines"));
        Assert.That(rows[3].Line, Is.EqualTo(6), "a line break inside quotes moves the next row's line number on");
        Assert.That(rows[3].Cells[1], Is.EqualTo("Say \"hi\""));
    }

    [Test]
    public void Reader_NeverThrows_OnAnUnclosedQuote()
    {
        var rows = CsvReader.Read("email\n\"a@x.edu");
        Assert.That(rows[1].Cells[0], Is.EqualTo("a@x.edu"));
    }

    [Test]
    public void Clean_UndoesTheExportsFormulaGuard()
    {
        Assert.That(CsvReader.Clean(" '=SUM(A1) "), Is.EqualTo("=SUM(A1)"));
        Assert.That(CsvReader.Clean("O'Brien"), Is.EqualTo("O'Brien"));
    }

    [Test]
    public void Parse_ReadsColumnsInAnyOrder_WithAliases_AndListsTheOnesItIgnores()
    {
        var f = StudentCsv.Parse("Full Name,E-mail Address,Login,Temporary Password,Class\nAda,ada@x.edu,ada,Secret-pass-123,7B\n");
        Assert.That(f.Usable, Is.True);
        Assert.That(f.IgnoredColumns, Is.EqualTo(new[] { "Class" }));
        var r = f.Rows.Single();
        Assert.That((r.Line, r.Email, r.Name, r.UserName, r.Password, r.Error), Is.EqualTo((2, "ada@x.edu", "Ada", "ada", "Secret-pass-123", (string?)null)));
    }

    [Test]
    public void Parse_FlagsBadRows_AndKeepsTheGoodOnes()
    {
        var f = StudentCsv.Parse("email,name,username,password\n" +
                                 "ok@x.edu,Ok,,\n" +
                                 ",No email,,\n" +
                                 "not-an-email,Bad,,\n" +
                                 "OK@x.edu,Duplicate,,\n" +
                                 "u@x.edu,User without password,someone,\n" +
                                 $"long@x.edu,{new string('n', StudentCsv.MaxNameLength + 1)},,\n");
        Assert.That(f.Usable, Is.True);
        var errors = f.Rows.ToDictionary(r => r.Line, r => r.Error);
        Assert.That(errors[2], Is.Null);
        Assert.That(errors[3], Does.Contain("No email"));
        Assert.That(errors[4], Does.Contain("not an email"));
        Assert.That(errors[5], Does.Contain("line 2"), "emails are compared without letter case");
        Assert.That(errors[6], Does.Contain("needs a password"));
        Assert.That(errors[7], Does.Contain("longer than"));
    }

    [TestCase("", "empty")]
    [TestCase("name,username\nAda,ada", "\"email\" column")]
    [TestCase("email,Email\na@x.edu,b@x.edu", "appears twice")]
    [TestCase("email\n", "no students")]
    public void Parse_RefusesAFileItCannotUse(string text, string expected)
    {
        var f = StudentCsv.Parse(text);
        Assert.That(f.Usable, Is.False);
        Assert.That(f.FileErrors.Single(), Does.Contain(expected));
        Assert.That(f.Rows, Is.Empty);
    }

    [Test]
    public void Parse_CapsTheNumberOfRows()
    {
        var text = "email\n" + string.Join("\n", Enumerable.Range(0, StudentCsv.MaxRows + 1).Select(i => $"s{i}@x.edu"));
        var f = StudentCsv.Parse(text);
        Assert.That(f.Usable, Is.False);
        Assert.That(f.FileErrors.Single(), Does.Contain("at most"));
    }

    [Test]
    public void Line_QuotesAndGuardsCells()
    {
        Assert.That(StudentCsv.Line("a,b", "=1+1", null, 3), Is.EqualTo("\"a,b\",'=1+1,,3\r\n"));
    }

    [Test]
    public void Progress_FollowsTheAttemptFacts()
    {
        Assert.That(AdminPeople.Of(0, false, false, false, null), Is.EqualTo(AssignmentProgress.NotStarted));
        Assert.That(AdminPeople.Of(1, true, false, false, null), Is.EqualTo(AssignmentProgress.InProgress));
        Assert.That(AdminPeople.Of(1, false, false, false, null), Is.EqualTo(AssignmentProgress.Interrupted));
        Assert.That(AdminPeople.Of(1, false, true, false, null), Is.EqualTo(AssignmentProgress.AwaitingGrading));
        Assert.That(AdminPeople.Of(1, false, true, true, null), Is.EqualTo(AssignmentProgress.Graded));
        Assert.That(AdminPeople.Of(1, false, true, true, DateTimeOffset.UtcNow), Is.EqualTo(AssignmentProgress.Released));
        Assert.That(AdminPeople.Of(2, true, true, true, DateTimeOffset.UtcNow), Is.EqualTo(AssignmentProgress.InProgress), "a new attempt in progress comes first");
    }
}
