using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Flyback.App.Assist;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Controls;

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
public sealed class AssistantPanel : UserControl
{
    private static readonly IBrush Amber = new SolidColorBrush(Colors.Attention);

    /// <summary>The middle of <see cref="LogoMark"/>'s sweep, borrowed for the one thing here that is alive.</summary>
    private static readonly IBrush Live = new SolidColorBrush(Colors.Feedback);

    private readonly PluginCatalog plugins;
    private readonly Func<Patch> current;

    /// <summary>
    /// Where a Sample module's file is looked up, so what the assistant hears is
    /// what the editor plays. Null in a test, which makes every player silent.
    /// </summary>
    private readonly ISampleLibrary? samples;
    private readonly IImageLibrary? pictures;
    /// <summary>
    /// Hands a patch to the canvas, where it lands as an edit rather than as a
    /// new document. Nothing can refuse it and nothing needs to: an assistant's
    /// patch goes into the history like any other edit, so the way back out is
    /// the one every other edit already has.
    /// </summary>
    private readonly Action<Patch> apply;

    private readonly Action<string, string?> report;

    private readonly AssistantSettings settings;
    private readonly Credentials credentials;

    private readonly TextBox instruction = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Describe the patch you want. Enter to ask, Ctrl+Enter for a new line.",
        FontSize = Text.Body,
        MinHeight = 68,

        // A strip along the bottom for the send button to sit in. Reserved as
        // padding rather than left to overlap, so no line of a long message ever
        // runs underneath it — and across the whole width rather than down the
        // right-hand side, so every other line still has the box to itself.
        Padding = new Thickness(8, 6, 8, 34),
    };

    private readonly StackPanel saidPanel = new() { Spacing = 4, Margin = new Thickness(10, 8) };
    private readonly ScrollViewer transcript = new();

    /// <summary>
    /// The last frame the assistant looked at, and nothing at all until it has
    /// looked at one. Hidden rather than merely empty: a fixed width in an Auto
    /// column holds its 160 pixels open whether or not there is a picture in it,
    /// and that is a strip of dead panel beside the transcript and the
    /// instruction box for the whole of every run that never renders.
    /// </summary>
    private readonly Image lastFrame = new()
    {
        Width = 160,
        Stretch = Stretch.Uniform,
        IsVisible = false,
    };
    private readonly TextBlock footer = new()
    {
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
        Name = "footer",
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
        FontSize = Text.Small,
        Foreground = Live,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StackPanel working = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,
        Margin = new Thickness(0, 0, 0, 6),
        IsVisible = false,
    };

    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>What the one button shows in each of its two jobs.</summary>
    private const string SendGlyph = "⏎";

    private const string StopGlyph = "■";

    /// <summary>
    /// Send and stop, which are one button because they are never both offered: a
    /// turn is either wanted or under way, and the way to interrupt a run belongs
    /// where the hand that started it last was.
    /// </summary>
    /// <remarks>
    /// In the instruction box's own corner rather than out with Apply and
    /// Settings, because this is part of writing the message — and it is the first
    /// thing here to say that a message can be sent at all.
    /// </remarks>
    private readonly Button send = new()
    {
        Content = SendGlyph,
        Width = 30,
        Height = 26,
        Padding = new Thickness(0),
        FontSize = Text.Heading,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 7, 7),
        IsEnabled = false,
    };

    private readonly TextBox keyBox = new()
    {
        PasswordChar = '•',
        FontSize = Text.Body,
        Width = 260,
        HorizontalAlignment = HorizontalAlignment.Left,
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
        Width = 260,
        TextWrapping = TextWrapping.Wrap,

        // As the declared rows above it: a fixed width in a wider column is
        // centred unless it says otherwise, and a key block indented past the
        // form it sits under reads as a mistake.
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private readonly Button forget = new() { Content = "Forget key", Width = 100 };
    private readonly CheckBox rememberBox = new() { Content = "Keep this key", FontSize = Text.Body };

    /// <summary>
    /// Whether to keep a file of what gets sent and said. Off by default, since
    /// that is a second copy of everything a turn already sends somewhere else —
    /// see <see cref="AssistantSettings.LogConversations"/>.
    /// </summary>
    private readonly CheckBox logBox = new() { Content = "Log conversations to disk", FontSize = Text.Body };

    /// <summary>
    /// Everything the chosen provider says it has, drawn from its own declaration.
    /// This panel does not know what is on it: which model, which endpoint,
    /// whether there is an ear at all are the provider's questions (ADR-0069), and
    /// what arrives here is a bag of strings to hand back.
    /// </summary>
    private readonly AssistantForm form = new();

    private readonly ComboBox providerBox = new() { FontSize = Text.Body, Width = 260, Name = "provider" };

    private IPatchAssistant? assistant;
    private AssistantRun? run;

    /// <summary>
    /// Where <see cref="run"/>'s turns go when <see cref="AssistantSettings.LogConversations"/>
    /// asked for that. Starts closed, which writes nothing, so nothing here has
    /// to check the setting before every line.
    /// </summary>
    private ConversationLog log = ConversationLog.Start(false, string.Empty);

    /// <summary>
    /// What the conversation in <see cref="run"/> was started with. A session is
    /// built around one model at one endpoint and cannot be moved to another, so
    /// these are what say whether it is still the right conversation to be
    /// having — see <see cref="Restarting"/>.
    /// </summary>
    private AssistantConfig? runConfig;

    private IPatchAssistant? runAssistant;

    /// <summary>
    /// The paragraph the assistant is in the middle of, or null when it is not
    /// in the middle of one. Held rather than found, because what "the last
    /// block" is changes the moment anything else is written.
    /// </summary>
    private SelectableTextBlock? saying;

    /// <summary>Built the first time the settings window asks for it, and kept.</summary>
    private Control? section;

    /// <summary>
    /// Which provider was in force the moment the settings were opened, so a
    /// window closed without Save can be put back to it — see
    /// <see cref="DiscardSettings"/>.
    /// </summary>
    private string? openedProvider;

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

    /// <summary>
    /// Why a message cannot be sent, or null when one can. Kept rather than
    /// asked for, because the button is refreshed on every keystroke and the
    /// answer comes from the credential store.
    /// </summary>
    private string? blocked;
    private DateTime startedAt;
    private int pulse;

    /// <param name="report"></param>
    /// <param name="saved">
    /// The choices to open on, defaulting to the ones on this machine. Named only
    /// so a test can put a set in front of the panel without writing the file
    /// somebody is using.
    /// </param>
    /// <param name="plugins"></param>
    /// <param name="current"></param>
    /// <param name="apply"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    public AssistantPanel(
        PluginCatalog plugins,
        Func<Patch> current,
        Action<Patch> apply,
        Action<string, string?> report,
        AssistantSettings? saved = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null)
    {
        settings = saved ?? AssistantSettings.Load();
        this.samples = samples;
        this.pictures = pictures;
        this.plugins = plugins;
        this.current = current;
        this.apply = apply;
        this.report = report;

        credentials = new Credentials(plugins.PreferredSecretStore);
        assistant = Choose();

        Content = Build();

        // Filled in here rather than where the settings window is put together.
        // That window is built the first time somebody opens one, and what is
        // set on the form is what says whether a message can be sent at all —
        // which the footer has to answer from the moment the panel exists.
        rememberBox.IsChecked = settings.RememberKey;
        logBox.IsChecked = settings.LogConversations;

        // The list before what is chosen in it, and both before the handler that
        // watches it: a box with no rows in it cannot be told which row to show,
        // and a selection made now is a restoration rather than a choice.
        providerBox.ItemsSource = plugins.Assistants.Select(a => a.Name).ToList();

        ShowProviderForm();

        form.Changed += (_, _) => Refresh();

        Refresh();
    }

    /// <summary>What the status bar should say about this. Never names a key.</summary>
    public string Summary => assistant is null ? "assistant: none" : $"assistant: {assistant.Name}";

    // --- building -----------------------------------------------------------

    private Control Build()
    {
        transcript.Content = saidPanel;
        transcript.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

        // Tunnelling, and both halves of the gesture answered here: the box would
        // otherwise take Enter for itself on the way back up, and what it does
        // with a modifier held is its business rather than something to bet the
        // one way of sending a message on.
        instruction.AddHandler(KeyDownEvent, Typed, RoutingStrategies.Tunnel);

        send.Click += (_, _) =>
        {
            if (!asking)
            {
                _ = AskAsync();
                return;
            }

            // Cancellation lands at the next thing the assistant does, which may
            // be a whole request away. Saying so beats a button that goes dead
            // and a panel that carries on as if nothing was asked of it.
            stopping = true;
            run?.Stop();
            Beat();
        };

        // Whether there is anything to send changes on every keystroke, and
        // asking the whole of Refresh that often would go to the credential
        // store for an answer it already has.
        instruction.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowSendState();
        };

        working.Children.Add(beacon);
        working.Children.Add(progress);
        heartbeat.Tick += (_, _) => Beat();

        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(working, Dock.Top);
        // The button floats over the corner of the box rather than sitting
        // beside it, so the two are one thing to lay out.
        var writing = new Panel();
        writing.Children.Add(instruction);
        writing.Children.Add(send);

        DockPanel.SetDock(writing, Dock.Bottom);
        DockPanel.SetDock(footer, Dock.Bottom);
        left.Children.Add(working);
        left.Children.Add(footer);
        left.Children.Add(writing);
        left.Children.Add(transcript);

        // Two columns, since the settings went to the toolbar and nothing else
        // here was ever a button: what is left is the conversation and, when
        // there has been one, the frame the assistant last looked at.
        var columns = new Grid
        {
            Margin = new Thickness(12, 10),
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            ],
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(lastFrame, 1);

        columns.Children.Add(left);
        columns.Children.Add(lastFrame);

        // No height of its own. What this is worth is entirely a matter of what
        // is being read — a one-line refusal or forty turns of transcript — so
        // the window owns the split and this only says how small is too small
        // for the instruction box and a button to both still be there.
        return new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 140,
            Child = columns,
        };
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

        openedProvider = assistant?.Id;
        section ??= BuildSettings();

        // Taken back from whoever last borrowed it, rather than left to them to
        // return. A control has one parent and a closed window still holds the
        // one it was showing, so without this the settings would open exactly
        // once a session and throw on the second try.
        if (section.Parent is ContentControl lender) lender.Content = null;

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
            if (providerBox.SelectedIndex < 0 || providerBox.SelectedIndex >= plugins.Assistants.Count) return;

            assistant = plugins.Assistants[providerBox.SelectedIndex];
            settings.Provider = assistant.Id;

            // What the last provider was set to is kept rather than carried
            // over. A setting means whatever the provider that declared it says
            // it means, and the two need not agree about anything but the name.
            ShowProviderForm();
            Refresh();
        };

        // Its own padding, because it is the whole of a window now rather than a
        // flyout hanging off the button that opened it.
        var fields = new StackPanel { Spacing = 8, Margin = new Thickness(18), Width = 280 };

        fields.Children.Add(Text.Quiet("Provider"));
        fields.Children.Add(providerBox);
        fields.Children.Add(form);
        fields.Children.Add(Text.Quiet("API key"));
        fields.Children.Add(keyBox);
        fields.Children.Add(keyNote);
        fields.Children.Add(rememberBox);
        fields.Children.Add(logBox);

        var save = new Button { Content = "Save", Width = 84 };
        save.Click += (_, _) =>
        {
            SaveSettings();

            // Saving is the end of the errand, so the window goes with it.
            // Forgetting a key is not: somebody who has just taken one out is as
            // likely as not about to put another in.
            Dialog.Close(save, true);
        };

        forget.Click += (_, _) =>
        {
            if (assistant is null) return;

            credentials.Forget(assistant.Id);
            keyBox.Text = string.Empty;
            Refresh();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(save);
        row.Children.Add(forget);
        fields.Children.Add(row);

        return fields;
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
        settings.Provider = openedProvider ?? string.Empty;
        assistant = Choose();

        keyBox.Text = string.Empty;
        rememberBox.IsChecked = settings.RememberKey;
        logBox.IsChecked = settings.LogConversations;

        ShowProviderForm();
        Refresh();
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
            _ = AskAsync();
            return;
        }

        var text = instruction.Text ?? string.Empty;
        var from = Math.Clamp(Math.Min(instruction.SelectionStart, instruction.SelectionEnd), 0, text.Length);
        var to = Math.Clamp(Math.Max(instruction.SelectionStart, instruction.SelectionEnd), from, text.Length);

        instruction.Text = string.Concat(text.AsSpan(0, from), "\n", text.AsSpan(to));
        instruction.CaretIndex = from + 1;
    }

    // --- settings -----------------------------------------------------------

    private IPatchAssistant? Choose() =>
        (settings.Provider.Length > 0 ? plugins.Assistant(settings.Provider) : null)
        ?? plugins.PreferredAssistant;

    /// <summary>
    /// Puts the chosen provider's form up, holding what that provider was last set
    /// to. The provider box is set from here too, so the two cannot disagree about
    /// who is being configured; nothing else is, since the fields are asked for
    /// again after every answer.
    /// </summary>
    private void ShowProviderForm()
    {
        providerBox.SelectedIndex = assistant is null
            ? -1
            : plugins.Assistants
                .Select((a, i) => (a, i))
                .Where(pair => pair.a.Id == assistant.Id)
                .Select(pair => pair.i)
                .DefaultIfEmpty(-1)
                .First();

        form.Show(assistant is null ? null : assistant.Form, settings.Of(assistant?.Id ?? string.Empty));
    }

    private void SaveSettings()
    {
        settings.RememberKey = rememberBox.IsChecked == true;
        settings.LogConversations = logBox.IsChecked == true;

        if (assistant is not null)
        {
            settings.Provider = assistant.Id;
            settings.Remember(assistant.Id, form.Values);

            var keep = settings.RememberKey && credentials.CanKeep;

            if (!string.IsNullOrWhiteSpace(keyBox.Text))
            {
                credentials.Accept(assistant.Id, keyBox.Text, keep);

                // Emptied once it has been taken. Left there it would hold the
                // secret in a control for the life of the window, and the line
                // that says where the key actually lives could never appear.
                keyBox.Text = string.Empty;
                SayWhereTheKeyWent();
            }
            else if (keep && credentials.HasEntered(assistant.Id))
            {
                // Ticking the box after the fact, with nothing typed. The key is
                // already in hand and the field is empty because this emptied
                // it, so asking for the secret again would be this program's
                // fault presented as the person's problem.
                credentials.KeepWhatIsHeld(assistant.Id);
                SayWhereTheKeyWent();
            }
        }

        try
        {
            settings.Save();
        }
        catch (Exception ex)
        {
            report($"Could not save the assistant settings: {ex.Message}", AssistantSettings.File);
        }

        Refresh();
    }

    /// <summary>
    /// The provider, its form as it stands, and the key — which is all a
    /// configuration is. Nothing is interpreted on the way past: what a set of
    /// answers means is the provider's to work out, on the other side of this call.
    /// </summary>
    private AssistantConfig? Configured()
    {
        if (assistant is null) return null;

        var key = credentials.Of(assistant.Id, assistant.Credential.EnvironmentVariable) ?? string.Empty;

        return new AssistantConfig(key, form.Values);
    }

    /// <summary>
    /// Reworks what the buttons and the footer say. The footer speaks only when
    /// there is an excuse to give — no plugin, no key, or whatever else is
    /// blocking a send — and stays out of the way otherwise.
    /// </summary>
    private void Refresh()
    {
        var config = Configured();
        var excuse = assistant is null
            ? "No assistant plugin is installed. See the status bar for where plugins are looked for."
            : config is null ? null : Excuse(assistant, config);

        blocked = excuse;
        ShowSendState();

        // On the box, since the box is what sending is done from now. The footer
        // says the same thing in amber a few pixels below, so nothing is lost to
        // anybody who never hovers.
        ToolTip.SetTip(instruction, excuse);
        ShowKeyState();

        // The standing disclosure of what gets sent and where the key came from
        // lives in the status bar; the footer here only ever speaks up for
        // something actionable, so it disappears once there is nothing to excuse.
        footer.IsVisible = excuse is not null;

        if (excuse is not null)
        {
            footer.Text = excuse;
            footer.Foreground = Amber;
        }
    }

    /// <summary>
    /// The one button, in whichever of its two jobs applies. Reads and does not
    /// ask, because it runs on every keystroke. Dead until there is something to
    /// send, which says what the panel knew and never showed: an empty box or a
    /// missing key is why Enter appeared to do nothing.
    /// </summary>
    private void ShowSendState()
    {
        send.Content = asking ? StopGlyph : SendGlyph;

        send.IsEnabled = asking
            || (blocked is null && !string.IsNullOrWhiteSpace(instruction.Text));

        ToolTip.SetTip(send, asking
            ? "Stop — it ends at the next thing the assistant does"
            : blocked ?? "Ask  (Enter)");
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
        var bench = run?.Workbench;

        var done = bench is null || bench.ToolCalls == 0
            ? string.Empty
            : $" · {Tally(bench.ToolCalls, "tool call")}, {Tally(bench.Edits, "edit")}";

        progress.Text = stopping
            ? $"Stopping — it ends at the next thing the assistant does · {Spell(elapsed)}"
            : $"Working · {Spell(elapsed)}{done}";
    }

    private void StartWorking()
    {
        asking = true;
        stopping = false;
        startedAt = DateTime.UtcNow;
        pulse = 0;

        working.IsVisible = true;
        heartbeat.Start();
        Beat();
    }

    private void StopWorking()
    {
        heartbeat.Stop();
        asking = false;
        stopping = false;
        working.IsVisible = false;
    }

    private static string Spell(TimeSpan elapsed) => elapsed.TotalSeconds < 60d
        ? $"{elapsed.TotalSeconds:0}s"
        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";

    private static string Tally(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

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
        if (assistant is null)
        {
            keyNote.Text = string.Empty;
            forget.IsEnabled = false;
            return;
        }

        var variable = assistant.Credential.EnvironmentVariable;
        var source = credentials.SourceOf(assistant.Id, variable);

        keyBox.PlaceholderText = source switch
        {
            CredentialSource.Environment => $"A key is set, from {variable}",
            CredentialSource.Kept => "A key is set, and kept",
            CredentialSource.Session => "A key is set, for this window",
            _ => "Paste a key",
        };

        var overruled = credentials.HasEntered(assistant.Id)
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

            _ => assistant.Credential.Help,
        };

        // Nothing to forget, or nothing this could reach if it tried: an
        // environment variable is not this application's to remove.
        forget.IsEnabled = credentials.HasEntered(assistant.Id);
    }

    /// <summary>
    /// Which of the two happened, read back rather than assumed. ADR-0034's own
    /// warning is that "held for this run" and "saved" look identical until the
    /// next launch, and a program that appeared to save something and did not is
    /// worse than one that never offered.
    /// </summary>
    private void SayWhereTheKeyWent()
    {
        if (assistant is null) return;

        var source = credentials.SourceOf(assistant.Id, assistant.Credential.EnvironmentVariable);

        report(
            source switch
            {
                CredentialSource.Kept => $"Key saved, and kept by {credentials.Store?.Name}.",
                _ when !credentials.CanKeep =>
                    "Key saved, for this window only — nothing installed can keep one.",
                _ when settings.RememberKey =>
                    "Key saved, but it could not be kept — it will last this window only.",
                _ => "Key saved, for this window only.",
            },
            null);
    }

    private static string? Excuse(IPatchAssistant assistant, AssistantConfig config)
    {
        try
        {
            return assistant.Unavailable(config);
        }
        catch (Exception ex)
        {
            // Answering this must not throw. One that does has said no.
            return $"{assistant.Name} could not say whether it is ready: {ex.Message}";
        }
    }

    // --- asking -------------------------------------------------------------

    /// <summary>
    /// Why this message has to begin a new conversation, or null to carry the
    /// one already going.
    /// </summary>
    /// <remarks>
    /// Three reasons, and each of them is a conversation that could not honestly
    /// continue rather than a tidy-up. A run holds a copy of the patch it
    /// started from, so a patch edited underneath it would be quietly discarded
    /// by the next proposal. A run holds a session built around one model at one
    /// endpoint, so changed settings are a different correspondent. And a run
    /// has a turn budget, which is there to stop a conversation growing without
    /// end.
    /// </remarks>
    private string? Restarting(AssistantConfig config)
    {
        if (run is null) return string.Empty;
        if (run.Exhausted) return "That conversation had its turns. Starting another.";
        if (!ReferenceEquals(runAssistant, assistant) || runConfig != config)
            return "The settings changed, so this is a new conversation.";

        return run.EditedUnderneath(current())
            ? "The patch changed underneath, so this is a new conversation about the one on screen."
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

        if (because is null && run is { } going) return going;

        run?.Dispose();
        run = new AssistantRun(
            with, config, plugins.Modules, current(), samples: samples, pictures: pictures);
        runConfig = config;
        runAssistant = with;

        log.Dispose();
        log = ConversationLog.Start(settings.LogConversations, with.Id);

        // Said rather than silently done, and only where there was something to
        // lose: the transcript emptying is otherwise the only sign that the
        // thing being talked to has just been replaced.
        if (saidPanel.Children.Count > 0)
        {
            saidPanel.Children.Clear();
            if (because is { Length: > 0 }) Add(because, Text.Muted, Text.Small);
        }

        lastFrame.Source = null;
        lastFrame.IsVisible = false;

        return run;
    }

    private async Task AskAsync()
    {
        if (asking || assistant is null) return;
        if (Configured() is not { } config || Excuse(assistant, config) is not null) return;

        var wanted = instruction.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(wanted)) return;

        var conversation = Conversation(assistant, config);

        Asked(wanted);
        log.Write("you", wanted);

        instruction.Text = string.Empty;

        StartWorking();
        Refresh();

        try
        {
            await foreach (var happened in conversation.Ask(wanted))
            {
                Show(happened);

                // The tallies move as the workbench is driven, and an edit that
                // lands is the strongest sign of all that this is alive.
                Beat();
            }

            Deliver();
        }
        catch (Exception ex)
        {
            // AssistantRun already turns a provider's failure into an event, so
            // anything arriving here is the shell's own fault rather than a
            // plugin's — but the window still survives it.
            Add($"Something went wrong: {ex.Message}", Amber, Text.Small);
            report($"The assistant stopped: {ex.Message}", null);
        }
        finally
        {
            StopWorking();
            Refresh();
        }
    }

    private void Show(PatchEvent happened)
    {
        switch (happened)
        {
            case PatchEvent.Said said:
                Append(said.Text);
                log.Write("said", said.Text);
                break;

            case PatchEvent.Did did:
                Add(did.Summary, Text.Muted, Text.Small);
                log.Write("did", did.Summary);
                break;

            case PatchEvent.Saw saw:
                Add(saw.Caption, Text.Muted, Text.Small);
                Picture(saw.Png);
                log.Write("saw", saw.Caption);
                break;

            // The caption and nothing else. The WAV went to the model rather
            // than to the speakers, and a panel that started playing sound while
            // the patch under the cursor is already playing would be two things
            // at once — the transcript says a sound was rendered and heard,
            // which is what somebody watching this needs to know.
            case PatchEvent.Heard heard:
                Add(heard.Caption, Text.Muted, Text.Small);
                log.Write("heard", heard.Caption);
                break;

            case PatchEvent.Cost cost:
                Add(
                    $"{cost.Input} in ({cost.CacheRead} cached), {cost.Output} out.",
                    Text.Muted,
                    11);
                log.Write("cost", $"{cost.Input} in ({cost.CacheRead} cached), {cost.Output} out.");
                break;

            case PatchEvent.Proposed proposed:
                Add($"Proposed: {proposed.Summary}", Brushes.White, Text.Body);
                log.Write("proposed", proposed.Summary);
                break;

            case PatchEvent.Failed failed:
                Add(failed.Message, Amber, Text.Small);
                log.Write("failed", failed.Message);
                break;
        }

        transcript.ScrollToEnd();
    }

    private void Picture(byte[] png)
    {
        try
        {
            lastFrame.Source = new Bitmap(new MemoryStream(png));
            lastFrame.IsVisible = true;
        }
        catch
        {
            // A frame that will not decode is not worth the window, and it does
            // not get to claim the width either. The caption that came with it
            // is already in the transcript.
        }
    }

    private void Add(string text, IBrush color, double size)
    {
        // Anything else in the transcript ends the paragraph the assistant was
        // in the middle of. Without this, prose lands on the end of whatever
        // block happens to be last and of about the right size — which was the
        // person's own message, run together with the reply to it.
        saying = null;

        saidPanel.Children.Add(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = color,
            FontSize = size,
        });
    }

    /// <summary>
    /// What the person just asked for, set apart from what comes back.
    /// </summary>
    /// <remarks>
    /// A conversation is kept now rather than cleared per message, so the two
    /// sides have to be told apart by looking: a gap above, and the accent the
    /// rest of the panel uses for its own voice.
    /// </remarks>
    private void Asked(string text)
    {
        saying = null;

        saidPanel.Children.Add(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = Text.Body,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, saidPanel.Children.Count > 0 ? 14 : 0, 0, 2),
        });
    }

    /// <summary>Streamed prose arrives in pieces, so it lands on the end of the last one.</summary>
    private void Append(string text)
    {
        if (saying is not null)
        {
            saying.Text += text;
            return;
        }

        saying = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = Text.Body,
        };

        saidPanel.Children.Add(saying);
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
    /// Editing the patch while a turn ran takes nothing to overrule: the
    /// edits are replaced, and saying so is enough, because one press of
    /// undo has them back.
    /// </para>
    /// </remarks>
    private void Deliver()
    {
        if (run?.Proposal is not { } proposed) return;

        // A turn somebody stopped is not one to act on. What it reached is in
        // the transcript, and asking again is a keystroke.
        if (stopping) return;

        var overwrote = run.EditedUnderneath(current());

        apply(proposed);

        // What this run just put on the canvas is not somebody editing behind
        // it, so the next message carries on the same conversation rather than
        // starting one about a patch it does not remember building.
        run.Rebase(proposed);

        Add(overwrote
            ? "Applied — this replaced the edits you made while it ran. Ctrl+Z puts them back."
            : "Applied. Ctrl+Z puts the patch back as it was.", Text.Muted, 11);

        report(string.Empty, null);
    }
}
