using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Flyback.Assist;
using Flyback.Assist.Keys;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Editor.Composition;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

/// <summary>
/// The editor opened on the assistant's column, with the fake assistants the panel
/// tests hand it.
/// </summary>
/// <remarks>
/// Mostly with no assistant. Where one is needed a fake is handed in: the half of
/// this panel that reacts to what an assistant can do cannot be looked at in front
/// of none.
/// </remarks>
public abstract class AssistantPanelTest : EditorTest
{
    /// <summary>
    /// Where a panel under test writes settings to, so pressing the real Save
    /// button in a test cannot land on the machine's own <c>assistant.json</c>.
    /// </summary>
    private protected readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-panel-settings-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    private protected EditorFolders Kept => new() { SettingsPath = settingsPath };

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The editor's window on Plasma with the assistant's column open. Only the
    /// settings it opens on, and a conversation where one is given, are the test's own.
    /// </summary>
    private protected MainWindow Showing(
        Assistants? assistants = null,
        AssistantSettings? saved = null,
        string? logs = null,
        AssistantConversation? conversation = null,
        SecretStores? stores = null)
    {
        var folders = Kept with { ConversationLogFolder = logs };

        return Column(Open(
            Presets.Plasma(NodeCatalog.BuiltIn),
            new EditorSetup { Assistants = assistants ?? Assistants.None, SecretStores = stores ?? SecretStores.None, Folders = folders },
            services =>
            {
                services.AddSingleton(new AssistantSettingRepository(folders, saved ?? new AssistantSettings()));
                if (conversation is not null) services.AddSingleton(conversation);
            }));
    }

    /// <summary>Opens the assistant's column in <paramref name="window"/>.</summary>
    private protected MainWindow Column(MainWindow window)
    {
        Service<Reactions>(window).Raise(new AssistantAsked(true));
        Settle(window);

        return window;
    }

    /// <summary>The assistant's column in <paramref name="window"/>.</summary>
    private protected static AssistantPanel PanelOf(Window window) => All<AssistantPanel>(window).Single();

    /// <summary>What a block reads as, whether it holds plain text or the runs its Markdown drew.</summary>
    /// <remarks>A line break reads as the platform's newline, so it is read back as <c>\n</c>.</remarks>
    private protected static string? Says(TextBlock block) => (block.Inlines is { Count: > 0 } runs ? runs.Text : block.Text)?.ReplaceLineEndings("\n");

    /// <summary>
    /// One assistant, which is the only way to see the half of this panel that
    /// reacts to what an assistant can do.
    /// </summary>
    private protected static Assistants With(IPatchAssistant assistant) => new([assistant]);

    // --- long blocks fold -----------------------------------------------------

    /// <summary>
    /// What a tool answers with: a caption, and then the patch written out, which
    /// is the length that made the transcript unreadable.
    /// </summary>
    private protected static string Machinery() => "Outrun: a whole synthwave track.\n"
        + string.Join('\n', Enumerable.Range(0, 40).Select(index => $"let value{index} = x |> value(v: {index})"));

    private protected static Button Fold(Window window) => All<Button>(window).Single(b => b.Name == "fold");

    /// <summary>What a fold says of its block, without the arrow. Read off its content, which a hidden fold never lays out.</summary>
    private protected static string Gist(Button fold) => Parts(fold).OfType<TextBlock>().Single(t => t.Name == "gist").Text ?? string.Empty;

    /// <summary>Whether a fold's arrow points down, at an open block.</summary>
    private protected static bool Opened(Button fold) => Parts(fold).Any(c => c.Name == "open");

    private protected static IEnumerable<Control> Parts(Button fold) => (fold.Content as Panel)?.Children ?? [];

    private protected static SelectableTextBlock Block(Window window, string text) =>
        All<SelectableTextBlock>(window).Single(block => Says(block) == text);

    // --- a frame the assistant looked at --------------------------------------

    /// <summary>One that renders, so the panel has a picture to place.</summary>
    private protected sealed class Looks() : Provider(new AssistantSchema(
        "looks",
        [new AssistantModel("looks")],
        "NONE",
        "none needed"))
    {
        public override string Id => "looks";

        public override string Name => "Renders once";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Look();
    }

    private protected sealed class Look : IPatchSession
    {
        public async IAsyncEnumerable<PatchEvent> Ask(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            await Task.Yield();

            yield return new PatchEvent.Saw(Frame(), "1 frame at 0.5s, 2 by 1.");
            yield return new PatchEvent.Said("that is a blue field.");
        }

        private static byte[] Frame()
        {
            var png = new MemoryStream();

            PngWriter.WriteBgra(png, new byte[2 * 1 * 4], 2, 1, 2 * 4);

            return png.ToArray();
        }

        public void Dispose()
        {
        }
    }

    private protected static Border Frame(Window window) =>
        All<Border>(window).Single(border => border.Name == "frame");

    // --- handbook text the assistant looked up --------------------------------

    private protected const string Looked = "core.filter | Filter | Filter";

    /// <summary>One that looks a module up and then says something.</summary>
    private protected sealed class Reads() : Provider(new AssistantSchema(
        "reads",
        [new AssistantModel("reads")],
        "NONE",
        "none needed"))
    {
        public override string Id => "reads";

        public override string Name => "Looks one module up";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Read();
    }

    private protected sealed class Read : IPatchSession
    {
        public async IAsyncEnumerable<PatchEvent> Ask(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            await Task.Yield();

            yield return new PatchEvent.Read(Looked);
            yield return new PatchEvent.Said("a filter it is.");
        }

        public void Dispose()
        {
        }
    }

    private protected Window Asked(AssistantSettings settings)
    {
        var window = Showing(With(new Reads()), settings);

        Instruction(window).Text = "make something";
        Settle(window);

        Press(SendButton(window));
        Settle(window);
        Settle(window);

        return window;
    }

    private protected static SelectableTextBlock Handbook(Window window) =>
        All<SelectableTextBlock>(window).Single(block => block.Text == Looked);

    /// <summary>The folded block the briefing arrives as.</summary>
    private protected static Control Briefing(Window window) =>
        (Control)All<Button>(window)
            .Single(b => b.Name == "fold" && Gist(b).Contains("The briefing it was handed"))
            .Parent!;

    /// <summary>The settings, in a window of their own, as opening them makes one.</summary>
    private protected Window Settings(Window panel)
    {
        var host = Owned(new Window { Content = All<AssistantPanel>(panel).Single().SettingsSection() });

        host.Show();
        Settle(host);

        return host;
    }

    /// <summary>
    /// The settings, as a provider that has already been configured leaves them.
    /// </summary>
    /// <remarks>
    /// The answers go in under the provider's own names, because that is the
    /// only shape the file has now — the App does not know a model from an
    /// endpoint, so it keeps a bag of strings per provider and hands it back
    /// (ADR-0069).
    /// </remarks>
    private protected static AssistantSettings Configured(string provider, params (string Key, string Value)[] answers)
    {
        var settings = new AssistantSettings { Provider = provider };

        settings.Remember(
            provider,
            new SettingValues(answers.ToDictionary(answer => answer.Key, answer => answer.Value)));

        return settings;
    }

    /// <summary>
    /// A provider of the ordinary shape, which declares the form its schema
    /// declares. What differs between the ones below is the models, which is
    /// what the form is drawn from.
    /// </summary>
    private protected abstract class Provider(AssistantSchema schema) : IPatchAssistant
    {
        public abstract string Id { get; }

        public abstract string Name { get; }

        public AssistantSchema Schema { get; } = schema;

        public KeyCredential Credential => Schema.Credential;

        public Uri Endpoint(SettingValues values) => new("https://assistant.test/");

        // Through Surveyed, as both shipped adapters declare themselves: a
        // survey of the endpoint replaces the written-down models, and a fake
        // that skipped it would be a fake nothing could probe.
        public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Surveyed(values).Form(values);

        public AssistantSenses Senses(SettingValues values) => Schema.Surveyed(values).Senses(values);

        public virtual string? Unavailable(AssistantConfig config) => null;

        public virtual IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            throw new NotSupportedException("this one is only ever asked what it can do.");
    }

    /// <summary>
    /// One provider that can see and one model that can hear, which is the shape
    /// every real provider here has: nothing does both.
    /// </summary>
    private protected sealed class Hearing() : Provider(new AssistantSchema(
        "sees",
        [new AssistantModel("sees"), new AssistantModel("hears", Vision: false, Hearing: true)],
        "NONE",
        "none needed"))
    {
        public override string Id => "hearing";

        public override string Name => "Can hear";
    }

    /// <summary>
    /// One provider whose model both sees and hears, which is what the Gemini
    /// adapter brought and nothing had before it.
    /// </summary>
    private protected sealed class Both() : Provider(new AssistantSchema(
        "both",
        [new AssistantModel("both", Hearing: true), new AssistantModel("deaf")],
        "NONE",
        "none needed"))
    {
        public override string Id => "both";

        public override string Name => "Sees and hears";
    }

    /// <summary>One with nothing that takes a sound, which most endpoints are.</summary>
    private protected sealed class Deaf() : Provider(new AssistantSchema(
        "quiet",
        [new AssistantModel("quiet")],
        "NONE",
        "none needed"))
    {
        public override string Id => "deaf";

        public override string Name => "Sees only";
    }

    private protected static Button SendButton(Window window) =>
        All<Button>(window).Single(b => b.Name == "send");

    /// <summary>The message box, told apart from the key field by taking newlines.</summary>
    private protected static TextBox Instruction(Window window) =>
        All<TextBox>(window).Single(b => b.AcceptsReturn);

    private protected static Rect On(Window window, Visual control) =>
        new(
            control.TranslatePoint(default, window) ?? throw new InvalidOperationException("not in this window"),
            control.Bounds.Size);

    private protected const string KeyVariable = "FLYBACK_PANEL_TEST_KEY";

    private protected const string Key = "sk-proj-paneltestpaneltestpanel42";

    /// <summary>One whose key is read from <see cref="KeyVariable"/>, and which answers once it has one.</summary>
    private protected sealed class Keyed() : Provider(new AssistantSchema(
        "keyed",
        [new AssistantModel("keyed")],
        KeyVariable,
        "none needed"))
    {
        public override string Id => "keyed";

        public override string Name => "Needs a key";

        public override string? Unavailable(AssistantConfig config) => config.Client.HasKey ? null : "no key";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            new Turn(Task.CompletedTask);
    }

    // --- a turn in flight -----------------------------------------------------

    /// <summary>
    /// One that answers when the test says so, which is the only way to look at a
    /// panel with a turn still running in it.
    /// </summary>
    private protected sealed class Held() : Provider(new AssistantSchema(
        "held",
        [new AssistantModel("held", Vision: false)],
        "NONE",
        "none needed"))
    {
        public TaskCompletionSource Release { get; } = new();

        public override string Id => "held";

        public override string Name => "Answers on cue";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            new Turn(Release.Task);
    }

    private protected sealed class Turn(Task release) : IPatchSession
    {
        public async IAsyncEnumerable<PatchEvent> Ask(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            await release;

            yield return new PatchEvent.Said("there you are.");
        }

        public void Dispose()
        {
        }
    }

    private protected static TextBlock Thinking(Window window) =>
        All<TextBlock>(window).Single(block => block.Name == "thinking");

    // --- probing the endpoint -------------------------------------------------

    /// <summary>
    /// One that can be asked what it offers, which is the half of the settings
    /// the probe button is for. It answers from here rather than over a network,
    /// and keeps what it was asked with.
    /// </summary>
    private protected sealed class Surveying(Func<CancellationToken, Task<IReadOnlyList<ModelReport>>>? answering = null)
        : Provider(new AssistantSchema("quiet", [new AssistantModel("quiet")], "NONE", "none needed")), IModelSurvey
    {
        public override string Id => "surveying";

        public override string Name => "Can be asked";

        /// <summary>What the last probe went out with, which is the whole question.</summary>
        public AssistantConfig? Asked { get; private set; }

        /// <summary>
        /// What it was asked about, once this has worked out what the caller
        /// meant. Resolved here because that is where it is resolved for real:
        /// the window says "the chosen one" and the provider says which.
        /// </summary>
        public SurveyOptions? Wanted { get; private set; }

        public Task<IReadOnlyList<ModelReport>> Survey(
            AssistantConfig config,
            SurveyOptions options,
            IProgress<string>? said = null,
            CancellationToken cancel = default)
        {
            Asked = config;
            Wanted = Schema.Asking(options, config.Values);

            // An endpoint that has whatever it was asked about, which takes both
            // kinds of input — so a report landing on the form is visible in it.
            return answering is null
                ? Task.FromResult<IReadOnlyList<ModelReport>>(
                    [.. (Wanted.Only ?? []).Select(id => new ModelReport(id) { Hearing = true })])
                : answering(cancel);
        }
    }

    /// <summary>A survey that answers only by being stopped.</summary>
    private protected static async Task<IReadOnlyList<ModelReport>> Never(CancellationToken cancel)
    {
        await Task.Delay(Timeout.Infinite, cancel);

        return [];
    }

    private protected static Button ProbeButton(Window host) =>
        All<Button>(host).Single(button => button.Name == "probe");

    private protected static string ProbeNote(Window host) =>
        All<TextBlock>(host).Single(block => block.Name == "probeNote").Text ?? string.Empty;

    /// <summary>The key field, told apart from the message box by hiding what is typed.</summary>
    private protected static TextBox KeyBox(Window host) =>
        All<TextBox>(host).Single(box => box.PasswordChar != default);

    /// <summary>A survey of two models, as a full probe leaves one behind.</summary>
    private protected static string Listed(params string[] models) =>
        Survey.Write(models.Select(id => new ModelReport(id)));

    /// <summary>One that is never ready, which is what a provider is until a key turns up.</summary>
    private protected sealed class Keyless() : Provider(new AssistantSchema(
        "only",
        [new AssistantModel("only")],
        "NONE",
        "none needed"))
    {
        internal const string Excuse = "No key yet — put one in Settings.";

        public override string Id => "keyless";

        public override string Name => "Wants a key";

        public override string Unavailable(AssistantConfig config) => Excuse;
    }

    // --- a conversation saved with the patch ----------------------------------

    /// <summary>
    /// A panel over one patch that stays the same object, as the canvas's does
    /// until somebody changes it — which is what a conversation saved with the
    /// patch is checked against.
    /// </summary>
    private protected (Window Window, AssistantPanel Panel) Over(Patch patch) => Over(() => patch);

    /// <summary>A panel over whatever patch the canvas holds, for an undo that hands back another object.</summary>
    /// <remarks>The conversation reads <paramref name="patch"/> rather than the canvas, so a test can hand it another object as an undo does.</remarks>
    private protected (Window Window, AssistantPanel Panel) Over(Func<Patch> patch)
    {
        var window = Column(Open(
            setup: new EditorSetup { Folders = Kept },
            replace: services => services.AddSingleton(new AssistantConversation(patch))));

        return (window, PanelOf(window));
    }

    private protected static string Saved(params TranscriptLine[] transcript) => Saved(null, transcript);

    private protected static string Saved(TokensSpent? tokens, params TranscriptLine[] transcript) => new SavedConversation(
        "gemini",
        SavedConversation.SettingsOf(SettingValues.None),
        1,
        new WorkbenchState("""{"nodes":[]}""", """{"nodes":[]}""", new Dictionary<string, Guid>(), 1, 2),
        null,
        transcript,
        Tokens: tokens).ToJson();

    private protected static ContextStrip Spent(Window window) => All<ContextStrip>(PanelOf(window)).Single();

    private protected static string Reading(Window window, string name) =>
        All<TextBlock>(PanelOf(window)).Single(block => block.Name == name).Text ?? string.Empty;

    private protected static List<string?> Counts(Window window) =>
        [.. All<TextBlock>(PanelOf(window)).Where(block => block.Name == "count").Select(Says)];

    private protected static List<string?> Shown(Window window) =>
        [.. All<SelectableTextBlock>(PanelOf(window)).Select(Says)];

    private protected static Button Fresh(Window window) => All<Button>(window).Single(b => b.Name == "fresh");

    // --- the working folds away -----------------------------------------------

    private protected static List<Button> Runs(Window window) => [.. All<Button>(window).Where(b => b.Name == "steps")];

    private protected static string Tally(Button run) => Parts(run).OfType<TextBlock>().Single(t => t.Name == "tally").Text ?? string.Empty;

    private protected static readonly TranscriptLine[] Worked =
    [
        new(Voice.You, "wire the room in"),
        new(Voice.Said, "Wiring it in."),
        new(Voice.Note, "added blipecho"),
        new(Voice.Note, "wired blip.out -> blipecho.in"),
        new(Voice.Aside, "75910 in, 3833 out"),
        new(Voice.Said, "Done."),
    ];
}
