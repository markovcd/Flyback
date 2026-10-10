using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Windows;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;

namespace Flyback.Editor.Tests.Decide;

/// <summary>The module list finds a module by what a phrase means, when what it spells finds nothing.</summary>
public sealed class PaletteByMeaningTests : EditorTest
{
    private MainWindow Opened(IDecisionModel? model) => Open(setup: new EditorSetup
    {
        Plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], decisionModels: model is null ? [] : [model]),
    });

    private static ModulePalette Palette(MainWindow window)
    {
        var editor = Editor(window);
        var at = editor.TranslatePoint(new Point(24, editor.Bounds.Height - 24), window)!.Value;

        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Settle(window);

        return All<ModulePalette>(window).Single();
    }

    private static void Type(MainWindow window, ModulePalette palette, string text)
    {
        All<TextBox>(palette).First().Text = text;
        Settle(window);
    }

    private static bool Likely(ModulePalette palette) => All<TextBlock>(palette).Any(t => t.Text == "LIKELY");

    [AvaloniaFact]
    public void A_phrase_lists_the_module_it_means_first_and_Enter_adds_it()
    {
        var window = Opened(new Kaleidoscopic());
        var palette = Palette(window);

        Type(window, palette, "a mirror maze of shards");

        Pump(() => Likely(palette), window);

        var first = All<Button>(palette).First(b => b.Content is "Kaleidoscope");
        first.Background.ShouldNotBeNull("the likeliest is the one Enter adds");

        All<TextBox>(palette).First().RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);

        Editor(window).Selection.Focused.ShouldNotBeNull().TypeId.ShouldBe("space.kaleidoscope");
    }

    [AvaloniaFact]
    public async Task Without_a_decision_model_a_phrase_finds_what_it_spells_and_nothing_more()
    {
        var window = Opened(null);
        var palette = Palette(window);

        Type(window, palette, "a mirror maze of shards");

        // Past the pause, when a decision model would have been asked.
        await Task.Delay(ModulePalette.Pause * 2);
        Dispatcher.UIThread.RunJobs();

        Likely(palette).ShouldBeFalse();
        All<TextBlock>(palette).ShouldContain(t => t.Text == "Nothing matches “a mirror maze of shards”.");
    }

    /// <summary>Takes every phrase to mean Geometry, and in it the Kaleidoscope.</summary>
    private sealed class Kaleidoscopic : IDecisionModel
    {
        public string Id => "kaleidoscopic";

        public string Name => "Kaleidoscopic";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) =>
            Task.FromResult(new Decision("k", request.Questions.ToDictionary(q => q.Key, q => (Answer)Chosen((Question.Choice)q.Value)), DecisionUsage.None));

        private static Answer.Chosen Chosen(Question.Choice choice)
        {
            // A module's label is its name, with its words in brackets after it.
            var labels = choice.Options.Select(o => o.Label).ToList();
            var wanted = labels.FirstOrDefault(l => l.StartsWith("Kaleidoscope (", StringComparison.Ordinal))
                ?? labels.FirstOrDefault(l => l == "Geometry")
                ?? "none";

            return new Answer.Chosen(wanted, choice.Options.ToDictionary(o => o.Label, o => o.Label == wanted ? 0.9 : 0.1 / choice.Options.Count), 0.9);
        }
    }
}
