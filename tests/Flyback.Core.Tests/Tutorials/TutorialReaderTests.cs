using Flyback.Engine.Tutorials;
using Shouldly;

namespace Flyback.Core.Tests.Tutorials;

public class TutorialReaderTests
{
    private const string Text = """
        # Lesson

        From: Empty

        What it builds,
        on two lines.

        ## Add one

        Press Space.

        ```fbks
        let wave = sine()
        ```

        ## Save

        Press Ctrl+S.
        """;

    [Fact]
    public void A_file_reads_as_its_title_preset_summary_and_steps()
    {
        var tutorial = TutorialReader.Read("lesson", Text);

        tutorial.ShouldSatisfyAllConditions(
            () => tutorial.Title.ShouldBe("Lesson"),
            () => tutorial.From.ShouldBe("Empty"),
            () => tutorial.Summary.ShouldBe("What it builds, on two lines."),
            () => tutorial.Steps.Select(s => s.Title).ShouldBe(["Add one", "Save"]),
            () => tutorial.Steps[0].Goal.ShouldBe("let wave = sine()"),
            () => tutorial.Steps[1].Goal.ShouldBeNull(),
            () => tutorial.Steps[1].Text.ShouldBe("Press Ctrl+S."));
    }

    [Theory]
    [InlineData("From: Empty\n## Step\n", "no '# title'")]
    [InlineData("# T\n## Step\n", "no 'From:' line")]
    [InlineData("# T\nFrom: Empty\n", "no steps")]
    [InlineData("# T\nFrom: Empty\n## Step\n```fbks\nsine()\n", "an unclosed goal in 'Step'")]
    [InlineData("# T\nFrom: Empty\n## Step\n```fbks\na\n```\n```fbks\nb\n```\n", "two goals in 'Step'")]
    public void A_misshapen_file_says_what_is_wrong(string text, string what) =>
        Should.Throw<FormatException>(() => TutorialReader.Read("t", text)).Message.ShouldBe($"Tutorial 't' has {what}.");
}
