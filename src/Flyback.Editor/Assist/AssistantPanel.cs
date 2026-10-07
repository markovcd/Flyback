using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Statistics;
using Flyback.Core;
using Flyback.Assist;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Assist;

/// <summary>
/// Where you describe a patch and watch one get built.
/// </summary>
/// <remarks>
/// A control rather than a method on the window, for the reason
/// <see cref="NodeEditor"/> is. It owns no patch: it is handed one when somebody
/// asks a question and hands one back when they accept a proposal, so a run that
/// is abandoned or bad costs nothing — everything between happens on
/// <see cref="AssistantRun"/>'s copy.
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The session ends with its conversation, in SetAside.")]
internal sealed class AssistantPanel : UserControl
{
    private static readonly IBrush Amber = new ImmutableSolidColorBrush(Colors.Attention);

    /// <summary>The middle of <see cref="LogoMark"/>'s sweep, borrowed for the one thing here that is alive.</summary>
    private static readonly IBrush Live = new ImmutableSolidColorBrush(Colors.Feedback);

    private static readonly IBrush LiveInk = new ImmutableSolidColorBrush(Colors.Blend(Colors.Feedback, Avalonia.Media.Colors.White, 0.3));
    private static readonly IBrush LiveGround = new ImmutableSolidColorBrush(Colors.Faded(Colors.Feedback, 0.13));
    private static readonly IBrush Idle = new ImmutableSolidColorBrush(Colors.Node);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Label);
    private static readonly IBrush Chrome = new ImmutableSolidColorBrush(Colors.Panel);
    private static readonly IBrush Rule = new ImmutableSolidColorBrush(Colors.Edge);

    /// <summary>The class the message box and the send button are styled by; see <see cref="Dress"/>.</summary>
    private const string Composing = "composing";

    private readonly PluginCatalog plugins;
    private readonly ChosenAssistant chosenAssistant;

    /// <summary>
    /// The patch the assistant edits and where it reports. What the assistant hears
    /// is what the editor plays, since its sounds are looked up where the editor's are.
    /// </summary>
    private readonly IAssistantEditor editor;
    private readonly AssistantConversation conversation;
    private readonly AssistantRunFactory runs;

    private readonly AssistantSettingRepository settingsRepository;

    /// <summary>
    /// Where <see cref="settings"/> is read from and written back to, and the priority
    /// list beside it. Null keeps them in memory only.
    /// </summary>
    private readonly string? settingsPath;
    private readonly string? logFolder;

    private readonly Credentials credentials;

    private readonly TextBox instruction = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Describe the patch you want",
        Name = "instruction",
        FontSize = Text.Body,
        MinHeight = 40,
        MaxHeight = 240,
        Padding = new Thickness(0, 2),
        Classes = { Composing },
    };

    private readonly TranscriptView transcript = new();

    private readonly TextBlock footer = new()
    {
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 0, 2, 8),
        Name = "footer",
    };

    /// <summary>How full the context is and what this conversation has cost, under the header.</summary>
    private readonly ContextStrip spent = new();

    /// <summary>Which model answered last, or which provider will, under the panel's name.</summary>
    private readonly TextBlock model = new()
    {
        Name = "model",
        FontSize = Text.Caption,
        FontFamily = Text.Mono,
        Foreground = Text.Muted,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// Proof that something is still happening.
    /// </summary>
    /// <remarks>
    /// A turn is minutes and most of it is spent waiting on a provider with
    /// nothing to show, so without this the panel cannot be told from one that has
    /// died. A beacon that moves says it in a way a label could not, since a label
    /// can as easily be stale.
    /// </remarks>
    private readonly Ellipse beacon = new()
    {
        Width = 7,
        Height = 7,
        Fill = Live,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock progress = new()
    {
        Name = "progress",
        FontSize = Text.Small,
        FontWeight = FontWeight.Medium,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The pill in the header holding the beacon: working, ready, or not set up.</summary>
    private readonly Border status = new()
    {
        Name = "status",
        CornerRadius = new CornerRadius(99),
        Padding = new Thickness(9, 4),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>What the one button shows in each of its two jobs.</summary>
    private readonly Control sendMark = Glyphs.Send();

    private readonly Control stopMark = Glyphs.Stop();

    /// <summary>
    /// Send and stop, which are one button because they are never both offered: a
    /// turn is either wanted or under way, and the way to interrupt a run belongs
    /// where the hand that started it last was.
    /// </summary>
    private readonly Button send = new()
    {
        Name = "send",
        Width = 34,
        Height = 34,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(8),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        IsEnabled = false,
        Classes = { Composing },
    };

    /// <summary>
    /// Sets the conversation aside for an empty one about the same patch.
    /// </summary>
    /// <remarks>
    /// Dead while a turn runs — stopping one is the send button's job — and when
    /// there is nothing to set aside.
    /// </remarks>
    private readonly Button fresh = new()
    {
        Content = Glyphs.Add(),
        Width = 30,
        Height = 30,
        Padding = new Thickness(0),
        Foreground = Text.Muted,
        Background = Brushes.Transparent,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsEnabled = false,
        Name = "fresh",
    };

    /// <summary>
    /// Has the assistant write the box out in full, in the box: as a change to the
    /// patch on the canvas, or as a new patch's brief when the canvas is empty.
    /// </summary>
    private readonly Button expand = new()
    {
        Content = "Expand",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        Background = Brushes.Transparent,
        Padding = new Thickness(8, 4),
        VerticalAlignment = VerticalAlignment.Center,
        IsEnabled = false,
        Name = "expand-message",
    };

    private readonly TextBox keyBox = new()
    {
        PasswordChar = '•',
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// What is under the key field, since the field itself cannot say it. A key
    /// that is set is never read back into the box (ADR-0034), and blank is also
    /// what "no key" looks like — so without this the one screen somebody opens to
    /// check cannot answer the question.
    /// </summary>
    private readonly TextBlock keyNote = new()
    {
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,

        // Under the box rather than under its caption, as a declared row's note is.
        Margin = new Thickness(InspectorRows.SettingsGutter, 0, 0, 0),
    };

    private readonly Button forget = new() { Content = "Forget key", FontSize = Text.Body };
    private readonly CheckBox rememberBox = new() { Content = "Keep this key", FontSize = Text.Body };

    /// <summary>
    /// The key label, box, note, "keep" box and "forget" button, together —
    /// every row here only means something in relation to a provider, so with
    /// none picked there is nothing for any of them to say.
    /// </summary>
    private readonly StackPanel keySection = new() { Spacing = 8 };

    /// <summary>The probe button and everything under it.</summary>
    private readonly ProbeSection probeSection;

    /// <summary>
    /// Whether to keep a file of what gets sent and said. Off by default, since
    /// that is a second copy of everything a turn already sends somewhere else —
    /// see <see cref="AssistantSettings.LogConversations"/>.
    /// </summary>
    private readonly CheckBox logBox = new() { Content = "Log conversations to disk", FontSize = Text.Body };

    /// <summary>Whether the briefing is shown — see <see cref="AssistantSettings.ShowBriefing"/>.</summary>
    private readonly CheckBox briefingBox = new()
    {
        Name = "showBriefing",
        Content = "Show the briefing it is handed",
        FontSize = Text.Body,
    };

    /// <summary>Whether what it looks up is shown — see <see cref="AssistantSettings.ShowLookups"/>.</summary>
    private readonly CheckBox lookupsBox = new()
    {
        Name = "showLookups",
        Content = "Show the handbook text it looks up",
        FontSize = Text.Body,
    };

    /// <summary>
    /// How many tokens a request may send — see
    /// <see cref="AssistantSettings.ContextLimit"/>. Stepped in tens of thousands,
    /// and a value typed with a fraction is rounded rather than refused.
    /// </summary>
    private readonly NumericUpDown contextBox = new()
    {
        Name = "contextLimit",
        Minimum = AssistantSettings.LeastContext,
        Maximum = AssistantSettings.MostContext,
        Increment = 10_000,
        FormatString = "0",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// How long the briefing may run before module descriptions are left out of
    /// it — see <see cref="AssistantSettings.ProseBudget"/>. Stepped in tens of
    /// thousands, since a few characters either way changes nothing.
    /// </summary>
    private readonly NumericUpDown proseBox = new()
    {
        Name = "proseBudget",
        Minimum = AssistantSettings.LeastProse,
        Maximum = AssistantSettings.MostProse,
        Increment = 10_000,
        FormatString = "0",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// What a module the assistant is not told about says so with, on the canvas
    /// and under its knobs alike.
    /// </summary>
    public const string UndescribedNote =
        "The assistant is not told what this module does. Every module's description "
        + "together would run past its budget, and this one is not on the priority list. "
        + "It can still look the module up when it needs to. The budget and the list are "
        + "in Settings → Assistant.";

    /// <summary>
    /// Type ids whose descriptions the assistant's briefing leaves out, for the
    /// canvas to mark. Empty with no provider chosen: nobody is being told
    /// anything, so nothing is being left out of it.
    /// </summary>
    public IReadOnlySet<string> Undescribed { get; private set; } = new HashSet<string>();

    /// <summary>
    /// Everything the chosen provider says it has, drawn from its own declaration.
    /// This panel does not know what is on it: which model, which endpoint,
    /// whether there is an ear at all are the provider's questions (ADR-0069), and
    /// what arrives here is a bag of strings to hand back.
    /// </summary>
    private readonly SettingsForm form = new() { Beside = true };

    private readonly ComboBox providerBox = new() { FontSize = Text.Body, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "provider" };

    /// <summary>
    /// The row that means no provider at all. First in the box and first among
    /// equals: picking it is a choice in its own right, not what is left when
    /// nothing else is, so it sits beside the real ones rather than replacing an
    /// empty selection.
    /// </summary>
    private const string NoProvider = "None";

    /// <summary>The conversation going, its log, and what the transcript is told of it.</summary>
    private readonly AssistantSession session;

    /// <summary>
    /// Told which provider a message went to, for the run's own count of itself
    /// (ADR-0094), and never what was asked. Null is nobody counting.
    /// </summary>
    private readonly Usage? usage;
    private readonly Reactions reactions;

    /// <summary>Built the first time the settings window asks for it, and kept.</summary>
    private Control? section;
    

    /// <summary>
    /// Whether a turn is in flight, as this panel knows it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="AssistantRun.Running"/>, which arrives too late to be one:
    /// <c>Ask</c> is an async iterator, so its body does not run until the first
    /// <c>MoveNextAsync</c> — after this panel has refreshed its buttons. This is
    /// set the moment somebody presses Enter.
    /// </remarks>
    private bool asking;

    private bool stopping;

    /// <summary>Stops a message being written out, in the box or before a patch starts from it; null when none is.</summary>
    private CancellationTokenSource? expanding;

    /// <summary>A note the next turn opens with, once its conversation has begun; null for none.</summary>
    private string? preface;

    /// <summary>The turn most recently begun, which a message sent from outside waits out.</summary>
    private Task turn = Task.CompletedTask;

    /// <summary>
    /// Why a message cannot be sent, or null when one can. Kept rather than
    /// asked for, because the button is refreshed on every keystroke and the
    /// answer comes from the credential store.
    /// </summary>
    private string? blocked;
    private DateTime startedAt;
    private int pulse;

    /// <param name="folders">Its <see cref="EditorFolders.SettingsPath"/> is where the settings are kept. Null keeps them in memory only.</param>
    /// <param name="saved">The settings to open on in place of the ones kept, for a test.</param>
    public AssistantPanel(
        ChosenAssistant chosenAssistant,
        PluginCatalog plugins,
        IAssistantEditor editor,
        AssistantConversation conversation,
        AssistantRunFactory runs,
        Credentials credentials,
        AssistantSettingRepository settingsRepository,
        EditorFolders? folders = null,
        Usage? usage = null,
        Reactions? reactions = null)
    {
        this.reactions = reactions ?? new Reactions();
        this.chosenAssistant = chosenAssistant;
        this.plugins = plugins;
        this.editor = editor;
        this.conversation = conversation;
        this.runs = runs;
        this.credentials = credentials;
        this.usage = usage;
        settingsPath = folders?.SettingsPath;
        logFolder = folders?.ConversationLogFolder;
        session = new AssistantSession(transcript, logFolder);
        this.settingsRepository = settingsRepository;
        conversation.Opened += Opened;
        conversation.Saved += (_, _) => this.reactions.Raise(new ConversationChanged());
        chosenAssistant.Load();
        probeSection = new ProbeSection(() => chosenAssistant.Value, KeyOnTheForm, form, Refresh);

        Content = Build();

        // Filled in here rather than where the settings window is put together.
        // That window is built the first time somebody opens one, and what is
        // set on the form is what says whether a message can be sent at all —
        // which the footer has to answer from the moment the panel exists.
        rememberBox.IsChecked = settingsRepository.Current.RememberKey;
        logBox.IsChecked = settingsRepository.Current.LogConversations;
        briefingBox.IsChecked = settingsRepository.Current.ShowBriefing;
        lookupsBox.IsChecked = settingsRepository.Current.ShowLookups;
        transcript.Shows(Voice.Briefing, settingsRepository.Current.ShowBriefing);
        transcript.Shows(Voice.Handbook, settingsRepository.Current.ShowLookups);
        contextBox.Value = settingsRepository.Current.ContextLimit;
        proseBox.Value = settingsRepository.Current.ProseBudget;

        Recount();

        // The list before what is chosen in it, and both before the handler that
        // watches it: a box with no rows in it cannot be told which row to show,
        // and a selection made now is a restoration rather than a choice.
        providerBox.ItemsSource = new[] { NoProvider }.Concat(plugins.Assistants.Select(a => a.Name)).ToList();

        ShowProviderForm();

        form.Changed += (_, _) => Refresh();

        Refresh();
    }
    
    // --- the conversation and the patch it is about ---------------------------

    /// <summary>
    /// A different document is on the canvas, with the conversation saved with it
    /// or with none (ADR-0072).
    /// </summary>
    /// <remarks>
    /// The conversation that was going ends here rather than at the next message:
    /// it was about the last document, and is saved with that one or not at all.
    /// One that arrived is shown at once and carried on by the next message, which
    /// is the moment there is a key and a configuration to carry it on with. Call
    /// this once the patch is on the canvas, since that patch is what an edit made
    /// before the first message is noticed against.
    /// </remarks>
    /// <param name="saved">What was saved with the document, as <see cref="SavedConversation.ToJson"/> wrote it.</param>
    public void Open(string? saved)
    {
        conversation.Open(saved);
    }

    private void Opened(object? sender, EventArgs e)
    {
        SetAside(clearConversation: false);

        if (conversation.Waiting is { } waiting)
        {
            foreach (var line in waiting.Transcript) transcript.Put(line.Voice, line.Text);

            transcript.Put(Voice.Note, "Saved with this patch. The next message carries this conversation on.", keep: false);
        }

        reactions.Raise(new ConversationChanged());
        ShowSendState();
        ShowSpent();
    }

    /// <summary>
    /// Sets the conversation on screen aside for an empty one about the same patch
    /// — see <see cref="fresh"/>.
    /// </summary>
    /// <remarks>
    /// Nothing on disk changes. A conversation saved with the patch stays there
    /// until the patch is saved again, which then writes whatever conversation
    /// there is by then, or none. Not while a turn runs: that is stopped first.
    /// </remarks>
    public void StartOver()
    {
        if (asking) return;

        SetAside();

        reactions.Raise(new ConversationChanged());
        ShowSendState();
        ShowSpent();
    }

    /// <summary>Ends whatever conversation there is, of either kind, and empties the panel of it.</summary>
    private void SetAside(bool clearConversation = true)
    {
        // A turn still running goes with the conversation it was part of.
        // Disposing the run stops it, and AskAsync drops whatever it still had
        // on its way.
        session.End();

        transcript.Clear();

        if (clearConversation) conversation.Clear();
    }

    /// <summary>
    /// The conversation to save with the patch on the canvas, or null where there
    /// is none, or where it is no longer about that patch.
    /// </summary>
    /// <remarks>
    /// "No longer about it" is the rule a message already follows: a module or wire
    /// changed underneath a conversation starts a new one (see <see cref="Restarting"/>), so
    /// saving the old one with it would only bring back, next time, a conversation
    /// that could not honestly go on.
    /// </remarks>
    public string? ConversationToSave() => conversation.ConversationToSave();
    
    /// <summary>
    /// Whether there is a turn in the conversation that saving the patch would keep
    /// and closing it would lose.
    /// </summary>
    public bool ConversationUnsaved => conversation.ConversationUnsaved;

    // --- starting from a prompt -----------------------------------------------

    /// <summary>Whether a message sent now would go: an assistant is chosen and has what it needs.</summary>
    public bool Ready => chosenAssistant.Value is { } assistant
        && Configured() is { } config
        && AssistantRun.Unready(assistant, config) is null;

    /// <summary>
    /// The brief the chosen assistant writes for a short idea, or why it wrote none.
    /// Asked of an empty patch and kept nowhere: it is not part of any conversation.
    /// </summary>
    public Task<(string? Brief, string? Failure)> ExpandAsync(string idea, CancellationToken cancel) =>
        WriteOutAsync(idea, new Flyback.Core.Graph.Patch(), cancel);

    /// <summary>
    /// The brief the chosen assistant writes for <paramref name="typed"/> over <paramref name="over"/>:
    /// a change to it, or a new patch where it holds only the Output.
    /// </summary>
    private async Task<(string? Brief, string? Failure)> WriteOutAsync(string typed, Flyback.Core.Graph.Patch over, CancellationToken cancel)
    {
        if (chosenAssistant.Value is not { } assistant || Configured() is not { } config)
            return (null, "No assistant is set up.");

        var writing = config with { Values = AssistantSchema.Expanding(config.Values) };

        if (AssistantRun.Unready(assistant, writing) is { } why) return (null, why);

        using var run = runs.Create(assistant, writing, over: over);

        return await PromptExpansion.ExpandAsync(run, typed, over, cancel);
    }

    /// <summary>Writes what is in the box out in full, over the patch on the canvas, for the person to edit before sending.</summary>
    private async Task ExpandMessageAsync()
    {
        var typed = instruction.Text ?? string.Empty;

        if (asking || string.IsNullOrWhiteSpace(typed)) return;

        using var stop = new CancellationTokenSource();

        expanding = stop;
        instruction.IsReadOnly = true;
        StartWorking();
        ShowSendState();

        (string? Brief, string? Failure) answer;

        try
        {
            answer = await WriteOutAsync(typed, editor.Current, stop.Token);
        }
        catch (OperationCanceledException)
        {
            transcript.Put(Voice.Note, "Stopped before the message was written out. The box holds it as typed.", keep: false);
            return;
        }
        finally
        {
            expanding = null;
            instruction.IsReadOnly = false;
            StopWorking();
            ShowSendState();
        }

        if (answer.Brief is { } brief)
        {
            instruction.Text = brief;
            instruction.CaretIndex = brief.Length;
        }
        else
        {
            transcript.Put(Voice.Note, $"The message could not be written out ({answer.Failure}). The box holds it as typed.", keep: false);
        }
    }

    /// <summary>
    /// Writes <paramref name="idea"/> out as a brief, saying so in the transcript, and
    /// sends the brief; the idea as typed where it could not be written out.
    /// </summary>
    public async Task StartFromIdeaAsync(string idea)
    {
        await turn;

        // Gone when the conversation begins, which empties the transcript; the preface says it again.
        transcript.Put(Voice.Note, "Writing the idea out as a brief first, and sending that.", keep: false);

        using var stop = new CancellationTokenSource();

        expanding = stop;
        StartWorking();

        (string? Brief, string? Failure) answer;

        try
        {
            answer = await ExpandAsync(idea, stop.Token);
        }
        catch (OperationCanceledException)
        {
            transcript.Put(Voice.Note, "Stopped before the idea was written out. Nothing was sent.");
            return;
        }
        finally
        {
            expanding = null;
            StopWorking();
        }

        preface = answer.Brief is null
            ? $"Started from the idea as typed, \"{idea}\": it could not be written out first ({answer.Failure})."
            : $"Started from the idea \"{idea}\", written out as the brief below.";

        await SendAsync(answer.Brief ?? idea);
    }

    /// <summary>
    /// Sends <paramref name="text"/> as the next message, once the turn that was going
    /// has ended, and returns when the new one has begun.
    /// </summary>
    public async Task SendAsync(string text)
    {
        await turn;

        instruction.Text = text;
        turn = AskAsync();
    }

    // --- building -----------------------------------------------------------

    private Control Build()
    {
        // Tunnelling, and both halves of the gesture answered here: the box would
        // otherwise take Enter for itself on the way back up, and what it does
        // with a modifier held is its business rather than something to bet the
        // one way of sending a message on.
        instruction.AddHandler(KeyDownEvent, Typed, RoutingStrategies.Tunnel);

        send.Click += (_, _) =>
        {
            if (!asking)
            {
                turn = AskAsync();
                return;
            }

            if (expanding is { } writing)
            {
                writing.Cancel();
                return;
            }

            // Cancellation lands at the next thing the assistant does, which may
            // be a whole request away. Saying so beats a button that goes dead
            // and a panel that carries on as if nothing was asked of it.
            stopping = true;
            session.Run?.Stop();
            Beat();
        };

        // Whether there is anything to send changes on every keystroke, and
        // asking the whole of Refresh that often would go to the credential
        // store for an answer it already has.
        instruction.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowSendState();
        };

        heartbeat.Tick += (_, _) => Beat();

        fresh.Click += (_, _) => StartOver();
        expand.Click += async (_, _) => await ExpandMessageAsync();

        Dress();

        var body = new DockPanel { Background = new SolidColorBrush(Colors.Window) };

        var top = new StackPanel();
        top.Children.Add(Header());
        top.Children.Add(spent);

        var head = new Border { Child = top, Background = Chrome, BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 0, 1) };
        var bottom = Composer();

        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        body.Children.Add(head);
        body.Children.Add(bottom);
        body.Children.Add(transcript);

        // No height of its own. What this is worth is entirely a matter of what
        // is being read — a one-line refusal or forty turns of transcript — so
        // the window owns the split and this only says how small is too small
        // for the instruction box and a button to both still be there.
        return new Border
        {
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 140,
            Child = body,
        };
    }

    /// <summary>The mark, the name and the model, the status pill, and a new conversation.</summary>
    private Control Header()
    {
        var mark = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Colors.Faded(Colors.Feedback, 0.14)),
            Child = Glyphs.Spark(16, Live),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        name.Children.Add(new TextBlock { Text = "Assistant", FontSize = Text.Emphasis, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
        name.Children.Add(model);

        var pill = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pill.Children.Add(beacon);
        pill.Children.Add(progress);
        status.Child = pill;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(12, 9, 6, 9),
        };

        header.Children.Add(mark);
        header.Children.Add(name);
        header.Children.Add(status);
        header.Children.Add(fresh);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(status, 2);
        Grid.SetColumn(fresh, 3);

        return header;
    }

    /// <summary>
    /// The box a message is written in, with the keys that send it, Expand and the
    /// send button along its foot, and above it whatever is stopping a send.
    /// </summary>
    private Control Composer()
    {
        var keys = Text.Quiet("Enter to ask · Ctrl+Enter for a new line", Text.Caption);
        keys.VerticalAlignment = VerticalAlignment.Center;
        keys.TextTrimming = TextTrimming.CharacterEllipsis;

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        foot.Children.Add(keys);
        foot.Children.Add(expand);
        foot.Children.Add(send);
        Grid.SetColumn(expand, 1);
        Grid.SetColumn(send, 2);

        var writing = new StackPanel { Spacing = 4 };
        writing.Children.Add(instruction);
        writing.Children.Add(foot);

        var field = new Border
        {
            Name = "composer",
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 6, 6),
            Child = writing,
        };

        // A click anywhere in the field is a click in the box.
        field.PointerPressed += (_, _) => instruction.Focus();

        var column = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };
        column.Children.Add(footer);
        column.Children.Add(field);

        return new Border
        {
            Background = Chrome,
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = column,
        };
    }

    /// <summary>
    /// Undresses the message box, which sits in a field of its own, and fills the
    /// send button. The theme sets both inside their templates, which only a style
    /// reaching the same part can override.
    /// </summary>
    private void Dress()
    {
        var box = new Style(x => x.OfType<TextBox>().Class(Composing).Template().OfType<Border>().Name("PART_BorderElement"));
        box.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        box.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));
        Styles.Add(box);

        // The quiet buttons stay quiet when they cannot be pressed: the glyph dims, no gray box.
        var off = new Style(x => x.OfType<Button>().Not(y => y.Class(Composing)).Class(":disabled")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        off.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent));
        off.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new ImmutableSolidColorBrush(Colors.Inactive)));
        Styles.Add(off);

        foreach (var (state, ground, ink) in new (string?, Color, Color)[]
                 {
                     (null, Colors.Label, Colors.Window),
                     (":pointerover", Colors.BeamCore, Colors.Window),
                     (":pressed", Colors.Value, Colors.Window),
                     (":disabled", Colors.Node, Colors.Inactive),
                 })
        {
            var style = new Style(x =>
            {
                var button = x.OfType<Button>().Class(Composing);
                if (state is not null) button = button.Class(state);
                return button.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            });

            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, new ImmutableSolidColorBrush(ground)));
            style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new ImmutableSolidColorBrush(ink)));
            Styles.Add(style);
        }
    }

    /// <summary>
    /// This panel's part of the settings window. Kept and lent out rather than
    /// built afresh, because these are fields with handlers already on them.
    /// </summary>
    /// <remarks>
    /// The credential state is read again every time it is asked for: a key can
    /// arrive or leave without this panel touching anything, and this is the one
    /// screen that claims to say which. What provider was in force is noted on the
    /// way out, so a window closed without Save can be put back to it — see
    /// <see cref="DiscardSettings"/>.
    /// </remarks>
    public Control SettingsSection()
    {
        Refresh();

        chosenAssistant.Load();
        section ??= BuildSettings();

        // Taken back from whoever last borrowed it, rather than left to them to
        // return. A control has one parent and a closed window still holds the
        // one it was showing, so without this the settings would open exactly
        // once a session and throw on the second try.
        switch (section.Parent)
        {
            case ContentControl lender: lender.Content = null; break;
            case Panel lender: lender.Children.Remove(section); break;
        }

        return section;
    }

    /// <summary>
    /// The window's contents: who is being talked to, what that one has to be
    /// told, and the key — in that order, because the middle changes entirely with
    /// the first. The provider and the key are the two the host owns; everything
    /// between them is the provider's own form.
    /// </summary>
    private Control BuildSettings()
    {
        providerBox.SelectionChanged += (_, _) =>
        {
            // Row 0 is always "None"; a real provider is one row below its own
            // place in plugins.Assistants because of it.
            var row = providerBox.SelectedIndex;
            if (row < 0 || row > plugins.Assistants.Count) return;

            chosenAssistant.Choose(row);
            
            // What the last provider was set to is kept rather than carried
            // over. A setting means whatever the provider that declared it says
            // it means, and the two need not agree about anything but the name.
            ShowProviderForm();
            Refresh();
        };

        // No padding of its own: it is one section of the settings window, which
        // pads the whole of it.
        var fields = new StackPanel { Spacing = 8, Width = SettingsSession.SectionWidth };

        keySection.Children.Add(InspectorRows.Field("API key", keyBox));
        keySection.Children.Add(keyNote);
        keySection.Children.Add(rememberBox);
        keySection.Children.Add(forget);

        fields.Children.Add(InspectorRows.Field("Provider", providerBox));

        // The probe goes on what is on the form, so a key typed and not yet
        // saved is a key it can use — and the button has to notice it arrive.
        keyBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) probeSection.ShowState();
        };

        fields.Children.Add(form);
        fields.Children.Add(keySection);
        fields.Children.Add(probeSection);
        ToolTip.SetTip(contextBox, "How many tokens a conversation may grow to before it stops.");
        fields.Children.Add(InspectorRows.Field("Context", contextBox));
        fields.Children.Add(logBox);
        fields.Children.Add(briefingBox);
        fields.Children.Add(lookupsBox);
        fields.Children.Add(InspectorRows.Field("Briefing", proseBox));
        fields.Children.Add(Note(
            "How many characters the briefing may run to. Past this, the modules on the priority list keep their descriptions, the rest keep "
            + "theirs while there is room, and the ones left out are marked on the canvas. The "
            + "list is a file, one type id a line, read again whenever settings are saved:"));

        // Selectable, so the path can be copied into a file manager or an editor.
        fields.Children.Add(new SelectableTextBlock
        {
            Name = "priorityFile",
            Text = settingsRepository.PriorityFile,
            FontSize = Text.Small,
            TextWrapping = TextWrapping.Wrap,
        });

        // Forgetting a key does not close the window the way Save does: somebody
        // who has just taken one out is as likely as not about to put another in.
        forget.Click += (_, _) =>
        {
            if (chosenAssistant.Value is null) return;

            credentials.Forget(chosenAssistant.Value.Id);
            keyBox.Text = string.Empty;
            Refresh();
        };

        return fields;

        static TextBlock Note(string text) => new()
        {
            Text = text,
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
        };
    }
    
    /// <summary>Works <see cref="Undescribed"/> out again, and says so if it moved.</summary>
    private void Recount()
    {
        var now = chosenAssistant.Value is null
            ? new HashSet<string>()
            : settingsRepository.GetProsePolicy().Undescribed(plugins.Modules);

        if (now.SetEquals(Undescribed)) return;

        Undescribed = now;
        reactions.Raise(new UndescribedChanged(Undescribed));
    }

    /// <summary>
    /// Puts back whatever was in force when the settings were opened, for a window
    /// closed some way other than Save.
    /// </summary>
    /// <remarks>
    /// Only the provider needs restoring by hand: picking one sets
    /// <see cref="AssistantSettings.Provider"/> immediately, so the form under it
    /// can change with it. Everything else is not written until
    /// <see cref="SaveSettings"/> runs, or was never kept at all — a key typed into
    /// <see cref="keyBox"/> only has to be blanked (ADR-0034). Internal rather than
    /// private because the UI tests need it.
    /// </remarks>
    internal void DiscardSettings()
    {
        probeSection.Stop();

        chosenAssistant.Load();

        keyBox.Text = string.Empty;
        rememberBox.IsChecked = settingsRepository.Current.RememberKey;
        logBox.IsChecked = settingsRepository.Current.LogConversations;
        briefingBox.IsChecked = settingsRepository.Current.ShowBriefing;
        lookupsBox.IsChecked = settingsRepository.Current.ShowLookups;
        contextBox.Value = settingsRepository.Current.ContextLimit;
        proseBox.Value = settingsRepository.Current.ProseBudget;

        ShowProviderForm();
        Refresh();
        Recount();
    }

    /// <summary>
    /// Enter asks; Ctrl+Enter — and Shift+Enter, which every other message box
    /// accepts — breaks the line. The break is put in by hand, because whether a
    /// <see cref="TextBox"/> types a newline for a gesture with a modifier held is
    /// Avalonia's own business.
    /// </summary>
    private void Typed(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;

        var breaking = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (!breaking)
        {
            turn = AskAsync();
            return;
        }

        var text = instruction.Text ?? string.Empty;
        var from = Math.Clamp(Math.Min(instruction.SelectionStart, instruction.SelectionEnd), 0, text.Length);
        var to = Math.Clamp(Math.Max(instruction.SelectionStart, instruction.SelectionEnd), from, text.Length);

        instruction.Text = string.Concat(text.AsSpan(0, from), "\n", text.AsSpan(to));
        instruction.CaretIndex = from + 1;
    }

    // --- settings -----------------------------------------------------------

    /// <summary>
    /// Puts the chosen provider's form up, holding what that provider was last set
    /// to. The provider box is set from here too, so the two cannot disagree about
    /// who is being configured; nothing else is, since the fields are asked for
    /// again after every answer.
    /// </summary>
    private void ShowProviderForm()
    {
        providerBox.SelectedIndex = chosenAssistant.Value is null
            ? 0
            : plugins.Assistants
                .Select((a, i) => (a, i))
                .Where(pair => pair.a.Id == chosenAssistant.Value.Id)
                .Select(pair => pair.i + 1)
                .DefaultIfEmpty(0)
                .First();

        form.Show(
            chosenAssistant.Value is null ? null : chosenAssistant.Value.Form, 
            settingsRepository.Current.Of(chosenAssistant.Value?.Id ?? string.Empty));
    }

    /// <summary>
    /// Makes what is on the section the panel's, and writes it out. Internal
    /// because the settings window's one Save button is the window's, not this
    /// panel's — see ADR-0082.
    /// </summary>
    internal void SaveSettings()
    {
        // What a probe still running would have found is not on the form yet, so
        // it is not what is being saved. It ends here rather than outliving the
        // window it was started from.
        probeSection.Stop();

        settingsRepository.Current.RememberKey = rememberBox.IsChecked == true;
        settingsRepository.Current.LogConversations = logBox.IsChecked == true;
        settingsRepository.Current.ShowBriefing = briefingBox.IsChecked == true;
        settingsRepository.Current.ShowLookups = lookupsBox.IsChecked == true;
        transcript.Shows(Voice.Briefing, settingsRepository.Current.ShowBriefing);
        transcript.Shows(Voice.Handbook, settingsRepository.Current.ShowLookups);

        // An emptied box keeps what was saved rather than becoming nought.
        if (contextBox.Value is { } context)
            settingsRepository.Current.ContextLimit = Math.Clamp((int)Math.Round(context), AssistantSettings.LeastContext, AssistantSettings.MostContext);

        contextBox.Value = settingsRepository.Current.ContextLimit;

        if (proseBox.Value is { } prose)
            settingsRepository.Current.ProseBudget = Math.Clamp((int)Math.Round(prose), AssistantSettings.LeastProse, AssistantSettings.MostProse);

        proseBox.Value = settingsRepository.Current.ProseBudget;

        // Into the conversation already going, as well as the next one: a limit
        // raised because a conversation ran out is raised for that conversation.
        if (session.Run is { } run) run.MaxContext = settingsRepository.Current.ContextLimit;

        settingsRepository.Current.Provider = chosenAssistant.Value?.Id ?? string.Empty;

        
        if (chosenAssistant.Value is not null)
        {
            settingsRepository.Current.Provider = chosenAssistant.Value.Id;
            settingsRepository.Current.Remember(chosenAssistant.Value.Id, form.Values);

            var keep = settingsRepository.Current.RememberKey && credentials.CanKeep;

            var origin = KeyedTransport.OriginOf(chosenAssistant.Value, form.Values);

            if (!string.IsNullOrWhiteSpace(keyBox.Text) && origin is null)
            {
                // Left in the box: a key is kept for the address it goes to, and there is none yet.
                editor.Report("Key not taken: the endpoint is not an address yet, and a key is kept for the one it goes to.", null);
            }
            else if (KeySafety.Refused(keyBox.Text) is { } refused)
            {
                editor.Report($"Key not taken: {refused}", null);
            }
            else if (!string.IsNullOrWhiteSpace(keyBox.Text))
            {
                credentials.Accept(chosenAssistant.Value.Id, keyBox.Text, origin!, keep);

                // Emptied once it has been taken. Left there it would hold the
                // secret in a control for the life of the window, and the line
                // that says where the key actually lives could never appear.
                keyBox.Text = string.Empty;
                SayWhereTheKeyWent();
            }
            else if (keep && credentials.SourceOf(chosenAssistant.Value.Id, chosenAssistant.Value.Credential.EnvironmentVariable) == CredentialSource.Session)
            {
                // Ticking the box after the fact, with nothing typed. The key is
                // already in hand and the field is empty because this emptied
                // it, so asking for the secret again would be this program's
                // fault presented as the person's problem. Session rather than
                // HasEntered: a key already Kept from an earlier save has
                // nothing left to do here, and saying so again on every later
                // Save would announce a change that did not happen.
                credentials.KeepWhatIsHeld(chosenAssistant.Value.Id);
                SayWhereTheKeyWent();
            }
        }

        try
        {
            settingsRepository.Save();
        }
        catch (Exception ex)
        {
            editor.Report($"Could not save the assistant settings: {ex.Message}", settingsPath);
        }

        Refresh();
        Recount();
    }

    /// <summary>
    /// The provider, its form as it stands, and what it sends over — which is all a
    /// configuration is. Nothing is interpreted on the way past: what a set of
    /// answers means is the provider's to work out, on the other side of this call.
    /// </summary>
    private AssistantConfig? Configured() =>
        chosenAssistant.Value is { } assistant
            ? new AssistantConfig(credentials.Transport(assistant, form.Values), form.Values)
            : null;

    /// <summary>
    /// Reworks what the buttons and the footer say. The footer speaks only when
    /// there is an excuse to give — no plugin, no key, or whatever else is
    /// blocking a send — and stays out of the way otherwise.
    /// </summary>
    private void Refresh()
    {
        var config = Configured();
        var excuse = chosenAssistant.Value is not null
            ? (config is null ? null : AssistantRun.Unready(chosenAssistant.Value, config))
            : plugins.Assistants.Count == 0
                ? "No assistant plugin is installed. See the status bar for where plugins are looked for."
                : "No assistant is selected. Pick one in Settings.";

        blocked = excuse;
        ShowSendState();

        // On the box, since the box is what sending is done from now. The footer
        // says the same thing in amber a few pixels below, so nothing is lost to
        // anybody who never hovers.
        ToolTip.SetTip(instruction, excuse);
        ShowKeyState();
        probeSection.ShowState();

        // The standing disclosure of what gets sent and where the key came from
        // lives in the status bar; the footer here only ever speaks up for
        // something actionable, so it disappears once there is nothing to excuse.
        footer.IsVisible = excuse is not null;

        if (excuse is not null)
        {
            footer.Text = excuse;
            footer.Foreground = Amber;
        }
        ShowSpent();
    }

    /// <summary>
    /// The conversation's cost in tokens: the one going, or the one saved with the
    /// patch and waiting to be carried on, or nothing where there is neither.
    /// </summary>
    private void ShowSpent()
    {
        var (tokens, turns) = session.Run is { } run
            ? (run.Tokens, run.Turns)
            : conversation.Waiting is { } waiting
                ? (waiting.Tokens ?? new TokensSpent(), waiting.Turns)
                : (new TokensSpent(), 0);

        spent.Show(tokens, turns, settingsRepository.Current.ContextLimit);
        model.Text = tokens.Model ?? chosenAssistant.Value?.Name ?? "No provider";
    }

    /// <summary>
    /// The one button, in whichever of its two jobs applies. Reads and does not
    /// ask, because it runs on every keystroke. Dead until there is something to
    /// send, which says what the panel knew and never showed: an empty box or a
    /// missing key is why Enter appeared to do nothing.
    /// </summary>
    private void ShowSendState()
    {
        send.Content = asking ? stopMark : sendMark;

        send.IsEnabled = asking
            || (blocked is null && !string.IsNullOrWhiteSpace(instruction.Text));

        expand.IsEnabled = !asking && blocked is null && !string.IsNullOrWhiteSpace(instruction.Text);

        ToolTip.SetTip(expand, "Have the assistant write this out in full, here, for you to edit before sending: "
            + "as a change to the patch on the canvas, or as a new patch's brief when the canvas is empty.");

        fresh.IsEnabled = !asking && (transcript.Lines.Count > 0 || session.Run is not null || conversation.Waiting is not null);

        ToolTip.SetTip(fresh, "Start a new conversation about this patch. The one set aside stays saved "
            + "with the patch until the patch is saved again.");

        ToolTip.SetTip(send, asking
            ? "Stop — it ends at the next thing the assistant does"
            : blocked ?? "Ask  (Enter)");

        ShowStatus();
    }

    /// <summary>The header's pill: working and for how long, ready, or not set up.</summary>
    private void ShowStatus()
    {
        if (asking) return;

        beacon.Opacity = 1;
        beacon.Fill = blocked is null ? Text.Muted : Amber;
        progress.Text = blocked is null ? "Ready" : "Not set up";
        progress.Foreground = Ink;
        status.Background = Idle;
        ToolTip.SetTip(status, blocked);
    }

    // --- showing that it is working -----------------------------------------

    /// <summary>
    /// One tick of the beacon. Everything here is read rather than remembered,
    /// so a tick missed while the dispatcher was busy costs nothing.
    /// </summary>
    private void Beat()
    {
        if (!asking) return;

        // A cosine rather than a blink: something that fades is obviously alive
        // and does not compete with the transcript for attention, which a thing
        // switching on and off twice a second would.
        pulse++;
        beacon.Opacity = 0.25d + 0.75d * ((Math.Cos(pulse * Math.PI / 5d) + 1d) / 2d);

        var elapsed = DateTime.UtcNow - startedAt;
        var bench = session.Run?.Workbench;

        var done = bench is null || bench.ToolCalls == 0
            ? string.Empty
            : $" · {TranscriptView.Tally(bench.ToolCalls, "tool call")}, {TranscriptView.Tally(bench.Edits, "edit")}";

        progress.Text = $"{(stopping ? "Stopping" : "Working")} · {Spell(elapsed)}";

        ToolTip.SetTip(status, stopping
            ? $"Stopping — it ends at the next thing the assistant does · {Spell(elapsed)}"
            : $"Working · {Spell(elapsed)}{done}");

        // Every third tick, so the dots are read as a rhythm rather than as a
        // flicker. Three of them, then none again.
        transcript.Thinking.Text = (stopping ? "Stopping" : "Thinking") + new string('.', pulse / 3 % 4);
    }

    private void StartWorking()
    {
        asking = true;
        conversation.Working = true;
        stopping = false;
        startedAt = DateTime.UtcNow;
        pulse = 0;

        beacon.Fill = Live;
        progress.Foreground = LiveInk;
        status.Background = LiveGround;
        transcript.Thinking.IsVisible = true;
        heartbeat.Start();
        Beat();
        transcript.ScrollToEnd();
    }

    private void StopWorking()
    {
        heartbeat.Stop();
        asking = false;
        conversation.Working = false;
        stopping = false;
        transcript.Thinking.IsVisible = false;
        ShowStatus();
    }

    private static string Spell(TimeSpan elapsed) => elapsed.TotalSeconds < 60d
        ? $"{elapsed.TotalSeconds:0}s"
        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";

    /// <summary>
    /// Says whether there is a key without ever showing one.
    /// </summary>
    /// <remarks>
    /// The three sources differ in what they promise and in what can be done
    /// about them, so each is named rather than reduced to a tick: one from the
    /// environment cannot be removed from here at all, and saying "forget it"
    /// under a button that would not is worse than saying nothing.
    /// </remarks>
    private void ShowKeyState()
    {
        keySection.IsVisible = chosenAssistant.Value is { NeedsKey: true };

        if (chosenAssistant.Value is null)
        {
            keyNote.Text = string.Empty;
            forget.IsEnabled = false;
            return;
        }

        var variable = chosenAssistant.Value.Credential.EnvironmentVariable;
        var source = credentials.SourceOf(chosenAssistant.Value.Id, variable);

        keyBox.PlaceholderText = source switch
        {
            CredentialSource.Environment => $"A key is set, from {variable}",
            CredentialSource.Kept => "A key is set, and kept",
            CredentialSource.Session => "A key is set, for this window",
            _ => "Paste a key",
        };

        var overruled = credentials.HasEntered(chosenAssistant.Value.Id)
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));

        // What a key entered here is standing on top of, since it is the reason
        // "Forget key" does something other than leave nothing behind.
        var falls = overruled ? $" Forget it to go back to {variable}." : string.Empty;

        keyNote.Text = source switch
        {
            CredentialSource.Session =>
                "In force, held for this window only and gone when it closes. It is never shown back "
                + "here — type a new one to replace it." + falls,

            CredentialSource.Kept =>
                $"In force, kept by {credentials.Store?.Name}. It is never shown back here — type a new "
                + "one to replace it." + falls,

            CredentialSource.Environment =>
                $"In force, from {variable}. {GlobalConstants.ApplicationName} never wrote it and never will. A key entered here "
                + "takes precedence over it, and forgetting that one comes back to this.",

            _ => chosenAssistant.Value.Credential.Help + " Make one for Flyback alone, with a spending limit.",
        };

        // Nothing to forget, or nothing this could reach if it tried: an
        // environment variable is not this application's to remove.
        forget.IsEnabled = credentials.HasEntered(chosenAssistant.Value.Id);
    }

    // --- probing the endpoint -----------------------------------------------

    /// <summary>
    /// The key the probe would go with: the one in the box before the one in
    /// hand, since a key typed here is not saved until Save and may under
    /// ADR-0034 never be written down at all.
    /// </summary>
    private IAssistantTransport? KeyOnTheForm()
    {
        if (chosenAssistant.Value is not { } assistant) return null;

        var transport = string.IsNullOrWhiteSpace(keyBox.Text)
            ? credentials.Transport(assistant, form.Values)
            : new KeyedTransport(keyBox.Text, KeyedTransport.OriginOf(assistant, form.Values), assistant.Credential);

        return transport.HasKey ? transport : null;
    }

    /// <summary>
    /// Which of the two happened, read back rather than assumed. ADR-0034's own
    /// warning is that "held for this run" and "saved" look identical until the
    /// next launch, and a program that appeared to save something and did not is
    /// worse than one that never offered.
    /// </summary>
    private void SayWhereTheKeyWent()
    {
        if (chosenAssistant.Value is null) return;

        var source = credentials.SourceOf(chosenAssistant.Value.Id, chosenAssistant.Value.Credential.EnvironmentVariable);
        var origin = credentials.Transport(chosenAssistant.Value, form.Values).Origin;

        var said = source switch
        {
            CredentialSource.Kept => $"Key saved, and kept by {credentials.Store?.Name}.",
            _ when !credentials.CanKeep =>
                "Key saved, for this window only — nothing installed can keep one.",
            _ when settingsRepository.Current.RememberKey =>
                "Key saved, but it could not be kept — it will last this window only.",
            _ => "Key saved, for this window only.",
        };

        if (KeySafety.Cleartext(origin))
            said += $" It goes to {origin} over plain http, readable on the way: fine for a server that takes any value, not for a real key.";

        editor.Report(said, null);
    }

    // --- asking -------------------------------------------------------------

    /// <summary>
    /// Why this message has to begin a new conversation, or null to carry the
    /// one already going.
    /// </summary>
    /// <remarks>
    /// Three reasons, and each of them is a conversation that could not honestly
    /// continue rather than a tidy-up. A run holds a copy of the patch it
    /// started from, so modules or wires changed underneath it would be quietly
    /// discarded by the next proposal; knobs are carried over instead (see
    /// <see cref="AssistantRun.CatchUp"/>). A run holds a session built around one model at one
    /// endpoint, so changed settings are a different correspondent. And a run
    /// has a turn budget, which is there to stop a conversation growing without
    /// end.
    /// </remarks>
    private string? Restarting(AssistantConfig config)
    {
        if (session.Run is not { } run) return conversation.Waiting is not { } waiting ? string.Empty : Unresumable(waiting, config);
        if (session.Spent(chosenAssistant.Value!, config) is { } spent) return spent;

        return run.Reshaped(editor.Current)
            ? "The modules or wires changed underneath, so this is a new conversation about the patch on screen."
            : null;
    }

    /// <summary>
    /// Why a conversation saved with the patch cannot be carried on, or null when it
    /// can: the three reasons <see cref="Restarting"/> gives, asked of what was saved
    /// rather than of a run.
    /// </summary>
    /// <remarks>
    /// The settings are compared by fingerprint, because that is all that was
    /// saved — see <see cref="SavedConversation"/>.
    /// </remarks>
    private string? Unresumable(SavedConversation saved, AssistantConfig config)
    {
        if (saved.Unresumable(settingsRepository.Current.ContextLimit, chosenAssistant.Value, config.Values) is { } why)
            return why;

        return conversation.WaitingMoved(editor.Current)
            ? "The modules or wires changed since it was opened, so this is a new conversation about the patch on screen."
            : null;
    }

    /// <summary>
    /// The conversation this message belongs to, which is the one already going
    /// unless it cannot be.
    /// </summary>
    /// <remarks>
    /// Keeping it is the whole of what a second message is for: the history, the
    /// workbench with everything built in it, and whatever prefix cache the
    /// provider holds for the briefing. A panel that started again per message
    /// is one where "you did not propose it" reaches a model with no memory of
    /// having built anything, and it answers — correctly, and uselessly — that
    /// it has not designed a patch yet.
    /// </remarks>
    private AssistantRun Conversation(IPatchAssistant with, AssistantConfig config)
    {
        var because = Restarting(config);

        if (because is null && session.Run is { } going)
        {
            going.CatchUp(editor.Current);
            return going;
        }

        // No run and no reason not to is a conversation saved with the patch,
        // which this message carries on.
        var resuming = because is null ? conversation.Waiting : null;
        conversation.Begin(resuming, editor.Current);

        var started = runs.Create(with, config, resuming);

        // A conversation of its own, so what was saved with the patch is no
        // longer what saving it again should write.
        session.Begin(started, with, config, settingsRepository.Current, resuming is not null, because);

        return started;
    }

    private async Task AskAsync()
    {
        if (asking || chosenAssistant.Value is null) return;
        if (Configured() is not { } config || AssistantRun.Unready(chosenAssistant.Value, config) is not null) return;

        var wanted = instruction.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(wanted)) return;

        var conversation = Conversation(chosenAssistant.Value, config);

        if (preface is { } note) transcript.Put(Voice.Note, note);
        preface = null;

        ShowSpent();

        usage?.Assistant(chosenAssistant.Value.Id);

        instruction.Text = string.Empty;

        StartWorking();
        Refresh();

        try
        {
            await foreach (var happened in session.Ask(wanted))
            {
                // A document arrived while this ran and took the conversation
                // with it (Open). What is still on its way is about that one.
                if (!ReferenceEquals(conversation, session.Run)) break;

                // The tallies move as the workbench is driven, and an edit that
                // lands is the strongest sign of all that this is alive.
                Beat();

                if (happened is PatchEvent.Cost) ShowSpent();
            }

            if (ReferenceEquals(conversation, session.Run))
            {
                Deliver();
                Settle();
            }
        }
        catch (Exception ex)
        {
            // AssistantRun already turns a provider's failure into an event, so
            // anything arriving here is the shell's own fault rather than a
            // plugin's — but the window still survives it.
            transcript.Put(Voice.Failed, $"Something went wrong: {ex.Message}");
            editor.Report($"The assistant stopped: {ex.Message}", null);
        }
        finally
        {
            StopWorking();
            Refresh();
        }
    }

    /// <summary>
    /// Takes the conversation as it stands now a turn has ended, as what saving the
    /// patch writes, and says there is something new to lose.
    /// </summary>
    private void Settle()
    {
        if (session.Run is null) return;

        conversation.Settle(session.Save());

        reactions.Raise(new ConversationChanged());
    }

    // --- accepting ----------------------------------------------------------

    /// <summary>
    /// Puts what the assistant made on the canvas, which is how every turn that
    /// made anything ends.
    /// </summary>
    /// <remarks>
    /// There is no button for it, and there is no button to take it back
    /// either. A proposal nobody applied is a proposal nobody can see, and
    /// because this arrives as an edit rather than as a new document, undo is
    /// the way back.
    /// <para>
    /// Knobs turned while a turn ran are kept wherever the assistant left the
    /// same knob alone. Modules or wires changed meanwhile are replaced, and
    /// saying so is enough, because one press of undo has them back.
    /// </para>
    /// </remarks>
    private void Deliver()
    {
        if (session.Run is not { Proposal: { } proposed } run) return;

        // A turn somebody stopped is not one to act on. What it reached is in
        // the transcript, and asking again is a keystroke.
        if (stopping) return;

        var overwrote = run.Reshaped(editor.Current);
        IReadOnlyList<Retuned> carried = overwrote ? [] : run.Merge(editor.Current);

        editor.Apply(proposed);

        // What this run just put on the canvas is not somebody editing behind
        // it, so the next message carries on the same conversation rather than
        // starting one about a patch it does not remember building. Whatever the
        // canvas made of the proposal, since a text document builds its own copy.
        run.Rebase(editor.Current);
        conversation.Rebase(editor.Current);

        transcript.Put(Voice.Aside, Applied(overwrote, carried));

        editor.Report(string.Empty, null);
    }

    private static string Applied(bool overwrote, IReadOnlyList<Retuned> carried)
    {
        if (overwrote) return "Applied — this replaced the modules and wires you changed while it ran. Ctrl+Z puts them back.";

        var kept = carried.Count(change => change.Kept);
        var overruled = carried.Count - kept;

        var keeping = kept == 0 ? string.Empty : $", keeping {TranscriptView.Tally(kept, "setting")} you changed while it ran";
        var standing = overruled == 0 ? string.Empty : $" Its own value stands for {TranscriptView.Tally(overruled, "setting")} you also changed.";

        return $"Applied{keeping}.{standing} Ctrl+Z puts the patch back as it was.";
    }
}
