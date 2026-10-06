using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Flyback.Assist;
using Flyback.Editor.Assist;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The assistant's transcript on its own, headless, fed the lines a turn would write.</summary>
[Binding]
public sealed class AssistantWorkingSteps(HeadlessTurn turn) : IDisposable
{
    private TranscriptView? transcript;

    private TranscriptView Transcript => transcript.ShouldNotBeNull();

    [Given("the assistant said {string}, did three things, and said {string}")]
    public void GivenSaidDidSaid(string first, string last) => Show(first, last);

    [Given("the assistant said {string} and did three things")]
    public void GivenSaidAndDid(string first) => Show(first, null);

    [When("the folded line is pressed")]
    public void WhenPressed() =>
        Headless.Run(() => Steps().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    [Then("the transcript has one folded line counting {int} steps")]
    public void ThenFolded(int count) => Counting(count, open: false);

    [Then("the transcript has one open line counting {int} steps")]
    public void ThenOpen(int count) => Counting(count, open: true);

    [Then("both sayings are outside it")]
    public void ThenOutside() =>
        Headless.Run(() => Sayings().ShouldAllBe(said => !said.GetLogicalAncestors().OfType<StepsGroup>().Any()));

    public void Dispose()
    {
        if (transcript is null) return;

        transcript = null;
        turn.Leave(this);
    }

    private void Show(string first, string? last)
    {
        turn.Take(this);

        Headless.Run(() =>
        {
            transcript = new TranscriptView();

            transcript.Put(Voice.Said, first);
            transcript.Put(Voice.Note, "added blipecho");
            transcript.Put(Voice.Note, "wired blip.out -> blipecho.in");
            transcript.Put(Voice.Aside, "75910 in, 3833 out");

            if (last is not null) transcript.Put(Voice.Said, last);
        });
    }

    private void Counting(int count, bool open) =>
        Headless.Run(() =>
        {
            var tally = Steps().Single().Content.ShouldBeAssignableTo<Panel>()!.Children
                .OfType<TextBlock>().Single(text => text.Name == "tally").Text;

            tally.ShouldBe($"{count} steps");
            Steps().Single().Content.ShouldBeAssignableTo<Panel>()!.Children.Any(part => part.Name == "open").ShouldBe(open);
        });

    private IEnumerable<Button> Steps() =>
        Transcript.GetLogicalDescendants().OfType<Button>().Where(button => button.Name == "steps");

    private IEnumerable<SelectableTextBlock> Sayings() =>
        Transcript.GetLogicalDescendants().OfType<SelectableTextBlock>().Where(block => block.Foreground == Avalonia.Media.Brushes.White);
}
