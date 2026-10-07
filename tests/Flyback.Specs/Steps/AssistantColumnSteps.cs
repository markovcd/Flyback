using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Flyback.Assist;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Editor;
using Flyback.Editor.Assist;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The assistant's column on its own, headless, with no provider and nothing sent.</summary>
[Binding]
public sealed class AssistantColumnSteps(HeadlessTurn turn) : IDisposable
{
    private readonly string settings = Path.Combine(Path.GetTempPath(), "flyback-column-" + Guid.NewGuid().ToString("N"), "settings.json");

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

        Headless.Run(() =>
        {
            var patch = new Patch();
            var catalog = PluginCatalog.Empty;
            var folders = new EditorFolders { SettingsPath = settings };
            var repository = new AssistantSettingRepository(folders, new AssistantSettings());
            var editor = new Holding(patch);
            var chosen = new ChosenAssistant(repository, catalog);
            var credentials = new Credentials(catalog.PreferredSecretStore);

            var panel = new AssistantPanel(
                chosen,
                catalog,
                editor,
                new AssistantConversation(() => patch),
                new AssistantRunFactory(catalog, editor, repository),
                credentials,
                repository,
                new AssistantSettingsPage(chosen, catalog, credentials, repository, editor, folders),
                folders);

            panel.Open(saved);
            shown = panel;
        });
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

        var folder = Path.GetDirectoryName(settings);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private void Message(bool open, string offer) =>
        Headless.Run(() =>
        {
            double.IsPositiveInfinity(Named<Panel>("message").MaxHeight).ShouldBe(open);
            Named<Button>("more").GetLogicalDescendants().OfType<TextBlock>().Single().Text.ShouldBe(offer);
        });

    private T Named<T>(string name) where T : Control =>
        Shown.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    /// <summary>An editor holding one patch, which takes nothing the assistant hands it.</summary>
    private sealed class Holding(Patch current) : IAssistantEditor
    {
        public Patch Current => current;

        public void Apply(Patch patch)
        {
        }

        public void Report(string message, string? detail)
        {
        }

        public ISampleLibrary? Samples => null;

        public IImageLibrary? Pictures => null;

        public IReadOnlyList<PatchPreset>? Presets() => null;
    }
}
