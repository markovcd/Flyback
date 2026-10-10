using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Flyback.Assist;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

public partial class AssistantPanelTests
{
    // --- a conversation saved with the patch ----------------------------------

    /// <summary>
    /// A panel over one patch that stays the same object, as the canvas's does
    /// until somebody changes it — which is what a conversation saved with the
    /// patch is checked against.
    /// </summary>
    private (Window Window, AssistantPanel Panel) Over(Patch patch) => Over(() => patch);

    /// <summary>A panel over whatever patch the canvas holds, for an undo that hands back another object.</summary>
    /// <remarks>The conversation reads <paramref name="patch"/> rather than the canvas, so a test can hand it another object as an undo does.</remarks>
    private (Window Window, AssistantPanel Panel) Over(Func<Patch> patch)
    {
        var window = Column(Open(
            setup: new EditorSetup { Folders = Kept },
            replace: services => services.AddSingleton(new AssistantConversation(patch))));

        return (window, PanelOf(window));
    }

    private static string Saved(params TranscriptLine[] transcript) => Saved(null, transcript);

    private static string Saved(TokensSpent? tokens, params TranscriptLine[] transcript) => new SavedConversation(
        "gemini",
        SavedConversation.SettingsOf(SettingValues.None),
        1,
        new WorkbenchState("""{"nodes":[]}""", """{"nodes":[]}""", new Dictionary<string, Guid>(), 1, 2),
        null,
        transcript,
        Tokens: tokens).ToJson();

    private static ContextStrip Spent(Window window) => All<ContextStrip>(PanelOf(window)).Single();

    private static string Reading(Window window, string name) =>
        All<TextBlock>(PanelOf(window)).Single(block => block.Name == name).Text ?? string.Empty;

    private static List<string?> Counts(Window window) =>
        [.. All<TextBlock>(PanelOf(window)).Where(block => block.Name == "count").Select(Says)];

    private static List<string?> Shown(Window window) =>
        [.. All<SelectableTextBlock>(PanelOf(window)).Select(Says)];

    [AvaloniaFact]
    public void A_conversation_saved_with_a_patch_is_shown_when_the_patch_opens()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(
            new TranscriptLine(Voice.You, "make a hard techno patch"),
            new TranscriptLine(Voice.Said, "Here is a kick at 150 bpm.")));
        Settle(window);

        Shown(window).ShouldContain("make a hard techno patch");
        Shown(window).ShouldContain("Here is a kick at 150 bpm.");
        panel.ConversationUnsaved.ShouldBeFalse("nothing has been said since it was opened");
    }

    [AvaloniaFact]
    public void A_conversation_saved_with_its_cost_shows_its_context_and_total_under_the_header()
    {
        var (window, panel) = Over(new Patch());

        Spent(window).IsVisible.ShouldBeFalse();

        panel.Open(Saved(new TokensSpent(2, 87_040, 80_000, 3_100, 45_000, "claude-opus-5-5"), new TranscriptLine(Voice.You, "hello")));
        Settle(window);

        Spent(window).IsVisible.ShouldBeTrue();
        Reading(window, "contextReading").ShouldBe("45k / 100k");
        Counts(window).ShouldBe(["87k", "80k", "3.1k", "1"]);
        Reading(window, "model").ShouldBe("claude-opus-5-5");

        panel.StartOver();
        Settle(window);

        Spent(window).IsVisible.ShouldBeFalse();
    }

    /// <summary>
    /// Draws the column with a conversation in it, every voice once, for looking at
    /// the panel without a provider. Run by hand with SHOT_DIR naming a folder.
    /// </summary>
    [AvaloniaFact]
    public void Draw_a_conversation()
    {
        var folder = Environment.GetEnvironmentVariable("SHOT_DIR");
        Assert.SkipWhen(folder is null, "a tool for the site's pictures, run with SHOT_DIR naming a folder to draw into");

        var (window, panel) = Over(new Patch());

        window.Width = 360;
        window.Height = 980;

        panel.Open(Saved(
            new TokensSpent(4, 147_200, 93_500, 18_300, 53_100, "claude-opus-5-5"),
            new TranscriptLine(Voice.You, string.Join("\n", Enumerable.Repeat(
                "The rider is drawn side-on, two wheels and a profile, while the grid races straight out toward the sun.", 6))),
            new TranscriptLine(Voice.Note, "Read the patch: 214 modules, 388 wires."),
            new TranscriptLine(Voice.Aside, "53083 in (51623 cached), 1121 out, from claude-opus-5-5."),
            new TranscriptLine(Voice.Said, "Side-on reads wrong because the grid converges on the sun, so the bike has to face away."),
            new TranscriptLine(Voice.Proposed, "Proposed: Adds a 'Picture: Rider' group: a dark motorbike seen from behind on the grid, bobbing on the kick."),
            new TranscriptLine(Voice.You, "Lower him a little."),
            new TranscriptLine(Voice.Failed, "The provider said the request was too large.")));
        Settle(window);

        Directory.CreateDirectory(folder);

        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("the window rendered nothing");

        frame.Save(Path.Combine(folder, "assistant-panel.png"), new PngBitmapEncoderOptions());
    }

    /// <summary>
    /// Saved again with the patch for as long as it is still about that patch. An
    /// edit before the first message ends it, as an edit under a run does.
    /// </summary>
    [AvaloniaFact]
    public void A_conversation_opened_with_a_patch_goes_with_it_until_the_patch_changes()
    {
        var patch = new Patch();
        var (_, panel) = Over(patch);

        panel.Open(Saved(new TranscriptLine(Voice.You, "hello")));
        panel.ConversationToSave().ShouldNotBeNull();

        patch.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0));

        panel.ConversationToSave().ShouldBeNull();
    }

    /// <summary>
    /// A knob turned is the same patch, and so is the copy an undo hands back: only a
    /// module or a wire makes it another.
    /// </summary>
    [AvaloniaFact]
    public void A_conversation_opened_with_a_patch_stays_with_it_through_a_knob_and_an_undo()
    {
        var patch = new Patch();
        var knob = NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0);
        patch.Nodes.Add(knob);
        patch.EnsureOutput();

        var (_, panel) = Over(() => patch);

        panel.Open(Saved(new TranscriptLine(Voice.You, "hello")));

        knob.InputValues[0] = 0.8f;
        panel.ConversationToSave().ShouldNotBeNull("a knob is not a new patch");

        patch = PatchIO.Read(PatchIO.ToJson(patch)).Patch;
        panel.ConversationToSave().ShouldNotBeNull("an undo hands back a copy of the same patch");
    }

    [AvaloniaFact]
    public void A_document_with_no_conversation_empties_the_panel()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(new TranscriptLine(Voice.You, "hello")));
        panel.Open(null);
        Settle(window);

        Shown(window).ShouldNotContain("hello");
        panel.ConversationToSave().ShouldBeNull();
    }

    [AvaloniaFact]
    public void Something_that_is_not_a_conversation_opens_as_none()
    {
        var (window, panel) = Over(new Patch());

        panel.Open("not a conversation");
        Settle(window);

        panel.ConversationToSave().ShouldBeNull();
        Shown(window).ShouldBeEmpty();
    }

    private static Button Fresh(Window window) => All<Button>(window).Single(b => b.Name == "fresh");

    [AvaloniaFact]
    public void There_is_no_new_conversation_to_start_with_none_to_set_aside()
    {
        var (window, _) = Over(new Patch());

        Fresh(window).IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// Set aside, and no longer what saving the patch writes: the new one is, and
    /// so far there is none. Nothing new has been said, so there is nothing to lose.
    /// </summary>
    [AvaloniaFact]
    public void A_new_conversation_sets_the_one_on_screen_aside()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(new TranscriptLine(Voice.You, "make a hard techno patch")));
        Settle(window);

        Fresh(window).IsEnabled.ShouldBeTrue();
        Press(Fresh(window));
        Settle(window);

        Shown(window).ShouldNotContain("make a hard techno patch");
        panel.ConversationToSave().ShouldBeNull();
        panel.ConversationUnsaved.ShouldBeFalse();
        Fresh(window).IsEnabled.ShouldBeFalse();
    }
}
