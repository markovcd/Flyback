using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Windows;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Gallery;

/// <summary>
/// The card at the head of the preset gallery that takes an idea, has the assistant
/// write it out in full, and starts a new patch from it.
/// </summary>
public class StartWithPromptTests : EditorTest
{
    /// <summary>An assistant that answers a request for a brief with one, and anything else with a short word.</summary>
    private sealed class Briefing(string? fails = null) : IPatchAssistant, IPatchSession
    {
        public const string Brief = "Night Freight: slow dub techno at 118 bpm in F minor, 48 bars, in five sections.";

        /// <summary>Every message it was sent, as it arrived.</summary>
        public ConcurrentQueue<string> Heard { get; } = [];

        public string Id => "briefing";

        public string Name => "Briefing";

        public int Priority => 0;

        public bool NeedsKey => false;

        public AssistantSchema Schema { get; } =
            new("briefing", [new AssistantModel("briefing")], "NONE", "none needed");

        public AssistantCredential Credential => Schema.Credential;

        public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

        public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Form(values);

        public AssistantSenses Senses(SettingValues values) => Schema.Senses(values);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => this;

        public async IAsyncEnumerable<PatchEvent> Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
        {
            Heard.Enqueue(instruction);

            await Task.Yield();

            if (fails is not null)
            {
                yield return new PatchEvent.Failed(fails);
                yield break;
            }

            yield return new PatchEvent.Said(instruction.Contains("build nothing") ? Brief : "On it.");
        }

        public void Dispose()
        {
        }
    }

    private MainWindow Open(Briefing? assistant)
    {
        var folders = new EditorFolders();
        var catalog = new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], assistant is null ? [] : [assistant]);

        return Open(
            setup: new EditorSetup { Folders = folders, Plugins = catalog },
            replace: services => services.AddSingleton(
                new AssistantSettingRepository(folders, new AssistantSettings { Provider = assistant?.Id ?? string.Empty })));
    }

    private static void OpenGallery(MainWindow window)
    {
        All<Button>(window).Single(b => b.Name == "presets-glyph")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    private static TextBox Words(MainWindow window) => All<TextBox>(window).Single(b => b.Name == "prompt-text");

    private static Button Press(MainWindow window, string name)
    {
        var button = All<Button>(window).Single(b => b.Name == name);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        return button;
    }

    private static void Until(Func<bool> done)
    {
        for (var attempt = 0; attempt < 400 && !done(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        done().ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_gallery_offers_a_prompt_card_only_when_an_assistant_is_set_up()
    {
        var without = Open(null);

        OpenGallery(without);
        All<Border>(without).ShouldNotContain(card => card.Name == "prompt-card");

        var with = Open(new Briefing());

        OpenGallery(with);
        All<Border>(with).ShouldContain(card => card.Name == "prompt-card");
    }

    [AvaloniaFact]
    public void Starting_closes_the_gallery_empties_the_patch_and_sends_the_prompt_written_out()
    {
        var assistant = new Briefing();
        var window = Open(assistant);
        var editor = All<NodeEditor>(window).Single();

        editor.History.Patch.Nodes.Count.ShouldBeGreaterThan(1, "the window opens on a preset");

        OpenGallery(window);

        Words(window).Text = "a slow dub track about a night train";
        Settle(window);

        Press(window, "start-prompt");

        Until(() => assistant.Heard.Count == 2);

        All<ModalOverlay>(window).ShouldBeEmpty("the gallery has closed");
        editor.History.Patch.Nodes.Count.ShouldBe(1, "the Empty preset is on the canvas");
        All<AssistantPanel>(window).Single().IsVisible.ShouldBeTrue();
        All<ToggleButton>(window).Single(b => b.Name == "assistant").IsChecked.ShouldBe(true);
        assistant.Heard.First().ShouldContain("build nothing", customMessage: "the idea is written out first");
        assistant.Heard.First().ShouldEndWith("a slow dub track about a night train");
        assistant.Heard.Last().ShouldEndWith(Briefing.Brief);
    }
}
