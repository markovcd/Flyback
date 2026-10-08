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
using Flyback.Ui.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Statistics;
using Flyback.Assist;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
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
    private readonly string? logFolder;

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
    /// <summary>The Assistant page of the settings window, whose form says whether a message can be sent at all.</summary>
    private readonly AssistantSettingsPage settings;

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

    /// <summary>The conversation going, its log, and what the transcript is told of it.</summary>
    private readonly AssistantSession session;

    /// <summary>
    /// Told which provider a message went to, for the run's own count of itself
    /// (ADR-0094), and never what was asked. Null is nobody counting.
    /// </summary>
    private readonly Usage usage;
    private readonly Reactions reactions;

    

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

    /// <param name="folders">Its <see cref="EditorFolders.ConversationLogFolder"/> is where conversations are logged; none keeps no log.</param>
    /// <param name="saved">The settings to open on in place of the ones kept, for a test.</param>
    public AssistantPanel(
        ChosenAssistant chosenAssistant,
        PluginCatalog plugins,
        IAssistantEditor editor,
        AssistantConversation conversation,
        AssistantRunFactory runs,
        AssistantSettingRepository settingsRepository,
        AssistantSettingsPage settings,
        EditorFolders folders,
        Usage usage,
        Reactions reactions,
        Decisions decisions)
    {
        this.reactions = reactions;
        this.chosenAssistant = chosenAssistant;
        this.plugins = plugins;
        this.editor = editor;
        this.conversation = conversation;
        this.runs = runs;
        this.usage = usage;
        logFolder = folders.ConversationLogFolder;
        session = new AssistantSession(transcript, logFolder, decisions);
        this.settingsRepository = settingsRepository;
        conversation.Opened += Opened;
        conversation.Saved += (_, _) => this.reactions.Raise(new ConversationChanged());
        this.settings = settings;
        settings.Changed += (_, _) => Refresh();

        Content = Build();

        transcript.Shows(Voice.Briefing, settingsRepository.Current.ShowBriefing);
        transcript.Shows(Voice.Handbook, settingsRepository.Current.ShowLookups);

        // Read by the canvas as the window is laid out, since nothing may be raised while it is built.
        Undescribed = Counted();

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
        && settings.Configured() is { } config
        && AssistantRun.Unready(assistant, config) is null;

    /// <summary>
    /// The brief the chosen assistant writes for a short idea, or why it wrote none.
    /// Asked of an empty patch and kept nowhere: it is not part of any conversation.
    /// </summary>
    public Task<(string? Brief, string? Failure)> ExpandAsync(string idea, CancellationToken cancel) =>
        WriteOutAsync(idea, new Core.Graph.Patch(), cancel);

    /// <summary>
    /// The brief the chosen assistant writes for <paramref name="typed"/> over <paramref name="over"/>:
    /// a change to it, or a new patch where it holds only the Output.
    /// </summary>
    private async Task<(string? Brief, string? Failure)> WriteOutAsync(string typed, Core.Graph.Patch over, CancellationToken cancel)
    {
        if (chosenAssistant.Value is not { } assistant || settings.Configured() is not { } config)
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

        foreach (var (state, ground, ink) in new[]
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

    /// <summary>This panel's page of the settings window, lent to the window that is opening.</summary>
    public Control SettingsSection()
    {
        Refresh();

        return settings.Section();
    }

    /// <summary>
    /// Puts back whatever was in force when the settings were opened, for a window
    /// closed some way other than Save. Internal rather than private because the
    /// UI tests need it.
    /// </summary>
    internal void DiscardSettings()
    {
        settings.Discard();

        Refresh();
        Recount();
    }

    /// <summary>
    /// Makes what is on the page the panel's, and writes it out. Internal because
    /// the settings window's one Save button is the window's, not this panel's —
    /// see ADR-0082.
    /// </summary>
    internal void SaveSettings()
    {
        settings.Save();

        transcript.Shows(Voice.Briefing, settingsRepository.Current.ShowBriefing);
        transcript.Shows(Voice.Handbook, settingsRepository.Current.ShowLookups);

        // Into the conversation already going, as well as the next one: a limit
        // raised because a conversation ran out is raised for that conversation.
        if (session.Run is { } run) run.MaxContext = settingsRepository.Current.ContextLimit;

        Refresh();
        Recount();
    }

    /// <summary>Works <see cref="Undescribed"/> out again, and says so if it moved.</summary>
    private void Recount()
    {
        var now = Counted();

        if (now.SetEquals(Undescribed)) return;

        Undescribed = now;
        reactions.Raise(new UndescribedChanged(Undescribed));
    }

    /// <summary>The modules the chosen assistant's briefing leaves undescribed, or none with no assistant chosen.</summary>
    private IReadOnlySet<string> Counted() => chosenAssistant.Value is null
        ? new HashSet<string>()
        : settingsRepository.GetProsePolicy().Undescribed(plugins.Modules);

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

    /// <summary>
    /// Reworks what the buttons and the footer say. The footer speaks only when
    /// there is an excuse to give — no plugin, no key, or whatever else is
    /// blocking a send — and stays out of the way otherwise.
    /// </summary>
    private void Refresh()
    {
        var config = settings.Configured();
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
        settings.ShowState();

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
        if (settings.Configured() is not { } config || AssistantRun.Unready(chosenAssistant.Value, config) is not null) return;

        var wanted = instruction.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(wanted)) return;

        var conversation = Conversation(chosenAssistant.Value, config);

        if (preface is { } note) transcript.Put(Voice.Note, note);
        preface = null;

        ShowSpent();

        usage.Assistant(chosenAssistant.Value.Id);

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
