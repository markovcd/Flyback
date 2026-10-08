using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Flyback.Assist;
using Flyback.Editor.Assist;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The assistant's column, headless, with no provider and nothing sent.</summary>
[Binding]
public sealed class AssistantColumnSteps(HeadlessTurn turn, EditorDriver editor) : IDisposable
{
    private Control? shown;
    private string? saved;

    private Control Shown => shown.ShouldNotBeNull();

    [Given("a message of {int} lines was sent to the assistant")]
    public void GivenLongMessage(int lines)
    {
        turn.Take(this);

        Headless.Run(() =>
        {
            var transcript = new TranscriptView();

            transcript.Put(Voice.You, string.Join("\n", Enumerable.Range(1, lines).Select(line => $"Line {line} of what is wanted.")));
            shown = transcript;
        });
    }

    [Given("a conversation that sent {int} tokens, {int} of them cached, wrote {int}, and last sent {int}")]
    public void GivenConversation(int input, int cached, int output, int context) =>
        saved = new SavedConversation(
            "gemini",
            SavedConversation.SettingsOf(SettingValues.None),
            1,
            new WorkbenchState("""{"nodes":[]}""", """{"nodes":[]}""", new Dictionary<string, Guid>(), 1, 2),
            null,
            [new TranscriptLine(Voice.You, "hello")],
            Tokens: new TokensSpent(1, input, cached, output, context)).ToJson();

    [When("the patch it was saved with is opened beside the assistant")]
    public void WhenOpened()
    {
        turn.Take(this);
        shown = editor.OpenAssistant(saved);
    }

    [When("{string} is pressed")]
    public void WhenPressed(string label) =>
        Headless.Run(() => Named<Button>("more").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    [When("the context line is pressed")]
    public void WhenContextPressed() =>
        Headless.Run(() => Named<Button>("context").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

    [Then("the message stands cut short, offering {string}")]
    public void ThenCut(string offer) => Message(open: false, offer);

    [Then("the whole message stands open, offering {string}")]
    public void ThenOpen(string offer) => Message(open: true, offer);

    [Then("the column shows {string} of context")]
    public void ThenContext(string reading) =>
        Headless.Run(() => Named<TextBlock>("contextReading").Text.ShouldBe(reading));

    [Then("the column shows {string} in, {string} cached, {string} out and {string} turn")]
    public void ThenCost(string input, string cached, string output, string turns) =>
        Headless.Run(() =>
        {
            Named<Control>("breakdown").IsVisible.ShouldBeTrue();
            Shown.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.Name == "count").Select(text => text.Text)
                .ShouldBe([input, cached, output, turns]);
        });

    public void Dispose()
    {
        if (shown is not null) turn.Leave(this);

        shown = null;
    }

    private void Message(bool open, string offer) =>
        Headless.Run(() =>
        {
            double.IsPositiveInfinity(Named<Panel>("message").MaxHeight).ShouldBe(open);
            Named<Button>("more").GetLogicalDescendants().OfType<TextBlock>().Single().Text.ShouldBe(offer);
        });

    private T Named<T>(string name) where T : Control =>
        Shown.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
}
