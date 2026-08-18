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
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Controls;

/// <summary>
/// Where you describe a patch and watch one get built.
/// </summary>
/// <remarks>
/// <para>
/// A control rather than a method on the window, for the reason
/// <see cref="NodeEditor"/> and <see cref="PreviewSurface"/> are: it has state
/// and behaviour of its own, and the window is long enough already.
/// </para>
/// <para>
/// It owns no patch. It is handed one when somebody asks a question and hands
/// one back when they accept a proposal — everything in between happens on
/// <see cref="AssistantRun"/>'s copy, so a run that is abandoned, cancelled or
/// simply bad costs nothing at all.
/// </para>
/// </remarks>
public sealed class AssistantPanel : UserControl
{
    private static readonly IBrush Dim = new SolidColorBrush(Colours.Muted);
    private static readonly IBrush Amber = new SolidColorBrush(Colours.Attention);

    /// <summary>The middle of <see cref="LogoMark"/>'s sweep, borrowed for the one thing here that is alive.</summary>
    private static readonly IBrush Live = new SolidColorBrush(Colours.Feedback);

    private readonly PluginCatalog plugins;
    private readonly Func<Patch> current;
    /// <summary>
    /// Hands a patch to the canvas, and says whether it got there. It may not:
    /// replacing the open patch is something the shell asks about first when
    /// there is unsaved work in it, and a refusal there has to leave this panel
    /// as it was rather than showing a proposal as applied.
    /// </summary>
    private readonly Func<Patch, Task<bool>> apply;

    private readonly Action<string, string?> report;

    private readonly AssistantSettings settings = AssistantSettings.Load();
    private readonly Credentials credentials;

    private readonly TextBox instruction = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Describe the patch you want. Enter to ask, Ctrl+Enter for a new line.",
        FontSize = 12,
        MinHeight = 64,
    };

    private readonly StackPanel said = new() { Spacing = 4, Margin = new Thickness(10, 8) };
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
    private readonly TextBlock footer = new() { FontSize = 11, Foreground = Dim, TextWrapping = TextWrapping.Wrap };

    /// <summary>
    /// Proof that something is still happening.
    /// </summary>
    /// <remarks>
    /// A turn is minutes, not seconds, and most of it is spent waiting on a
    /// provider with nothing to show: the transcript only fills in when an edit
    /// lands, and a rate limit is waited out in silence by design. Without this
    /// the panel is indistinguishable from one that has died — which is what
    /// makes a beacon that moves the point of it, rather than a label that could
    /// as easily be stale.
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
        FontSize = 11,
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

    private readonly Button stop = new() { Content = "Stop", Width = 96, IsEnabled = false };
    private readonly Button accept = new() { Content = "Apply", Width = 96, IsEnabled = false };
    private readonly Button revert = new() { Content = "Put it back", Width = 96, IsEnabled = false };

    private readonly TextBox keyBox = new() { PasswordChar = '•', FontSize = 12, Width = 260 };

    /// <summary>
    /// What is under the key field, since the field itself cannot say it.
    /// </summary>
    /// <remarks>
    /// A key that is set is never read back into the box — ADR-0034, and the
    /// reason the box is blank however many keys are in force. Blank is exactly
    /// what "no key" looks like too, though, so without this the one screen
    /// somebody opens to check cannot answer the question they opened it to ask.
    /// </remarks>
    private readonly TextBlock keyNote = new()
    {
        FontSize = 11,
        Foreground = Dim,
        Width = 260,
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly Button forget = new() { Content = "Forget key", Width = 100 };
    private readonly TextBox modelBox = new() { FontSize = 12, Width = 260 };
    private readonly TextBox baseUrlBox = new() { FontSize = 12, Width = 260 };
    private readonly CheckBox rememberBox = new() { Content = "Keep this key", FontSize = 12 };
    private readonly CheckBox visionBox = new() { Content = "Let it look at the picture", FontSize = 12 };
    private readonly ComboBox providerBox = new() { FontSize = 12, Width = 260 };
    private readonly ComboBox effortBox = new()
    {
        FontSize = 12,
        Width = 260,
        ItemsSource = new[] { "Low", "Medium", "High" },
    };

    private IPatchAssistant? assistant;
    private AssistantRun? run;
    private Patch? before;
    private bool warnedAboutEdits;

    /// <summary>
    /// Whether a turn is in flight, as this panel knows it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="AssistantRun.Running"/>, which is the run's own answer and
    /// arrives too late to be one: <c>Ask</c> is an async iterator, so its body
    /// does not run — and the flag inside it is not set — until the first
    /// <c>MoveNextAsync</c>, which happens after this panel has already refreshed
    /// its buttons. Asking the run left Stop dead for the whole of every turn.
    /// This is set the moment somebody presses Enter, which is the moment it
    /// becomes true from out here.
    /// </remarks>
    private bool asking;

    private bool stopping;
    private DateTime startedAt;
    private int pulse;

    public AssistantPanel(
        PluginCatalog plugins,
        Func<Patch> current,
        Func<Patch, Task<bool>> apply,
        Action<string, string?> report)
    {
        this.plugins = plugins;
        this.current = current;
        this.apply = apply;
        this.report = report;

        credentials = new Credentials(plugins.PreferredSecretStore);
        assistant = Choose();

        Content = Build();

        ReadSettingsIntoControls();
        Refresh();
    }

    /// <summary>What the status bar should say about this. Never names a key.</summary>
    public string Summary => assistant is null ? "assistant: none" : $"assistant: {assistant.Name}";

    // --- building -----------------------------------------------------------

    private Control Build()
    {
        transcript.Content = said;
        transcript.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

        // Tunnelling, and both halves of the gesture answered here: the box would
        // otherwise take Enter for itself on the way back up, and what it does
        // with a modifier held is its business rather than something to bet the
        // one way of sending a message on.
        instruction.AddHandler(KeyDownEvent, Typed, RoutingStrategies.Tunnel);

        stop.Click += (_, _) =>
        {
            // Cancellation lands at the next thing the assistant does, which may
            // be a whole request away. Saying so beats a button that goes dead
            // and a panel that carries on as if nothing was asked of it.
            stopping = true;
            run?.Stop();
            Beat();
        };

        accept.Click += async (_, _) => await Accept();
        revert.Click += async (_, _) => await Revert();

        var settingsButton = new Button { Content = "Settings", Width = 96 };
        var settingsFlyout = new Flyout
        {
            Content = BuildSettings(),
            Placement = PlacementMode.TopEdgeAlignedRight,
        };

        // Read again on the way open. A key can arrive or leave without this
        // panel touching anything — exported into the environment from another
        // window, or dropped into the store by something else — and this is the
        // one screen that claims to say which.
        settingsFlyout.Opened += (_, _) => Refresh();

        settingsButton.Flyout = settingsFlyout;

        // Sat on the bottom edge, level with the instruction box and the footer.
        // What is left here is reached for occasionally rather than every turn —
        // sending is a keystroke now — but it still belongs beside the box rather
        // than a panel's height away from it, which is a distance that changes
        // now the panel is draggable.
        var buttons = new StackPanel
        {
            Spacing = 6,
            Width = 96,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        buttons.Children.Add(stop);
        buttons.Children.Add(accept);
        buttons.Children.Add(revert);
        buttons.Children.Add(settingsButton);

        working.Children.Add(beacon);
        working.Children.Add(progress);
        heartbeat.Tick += (_, _) => Beat();

        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(working, Dock.Top);
        DockPanel.SetDock(instruction, Dock.Bottom);
        DockPanel.SetDock(footer, Dock.Bottom);
        left.Children.Add(working);
        left.Children.Add(footer);
        left.Children.Add(instruction);
        left.Children.Add(transcript);

        var columns = new Grid
        {
            Margin = new Thickness(12, 10),
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            ],
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(lastFrame, 1);
        Grid.SetColumn(buttons, 2);

        lastFrame.Margin = new Thickness(0, 0, 10, 0);

        columns.Children.Add(left);
        columns.Children.Add(lastFrame);
        columns.Children.Add(buttons);

        // No height of its own. What this is worth is entirely a matter of what
        // is being read — a one-line refusal or forty turns of transcript — so
        // the window owns the split and this only says how small is too small
        // for the instruction box and a button to both still be there.
        return new Border
        {
            Background = new SolidColorBrush(Colours.Panel),
            BorderBrush = new SolidColorBrush(Colours.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 140,
            Child = columns,
        };
    }

    private Control BuildSettings()
    {
        providerBox.ItemsSource = plugins.Assistants.Select(a => a.Name).ToList();
        providerBox.SelectionChanged += (_, _) =>
        {
            if (providerBox.SelectedIndex < 0 || providerBox.SelectedIndex >= plugins.Assistants.Count) return;

            assistant = plugins.Assistants[providerBox.SelectedIndex];
            settings.Provider = assistant.Id;
            modelBox.Text = assistant.Schema.DefaultModel;
            baseUrlBox.Text = assistant.Schema.DefaultBaseUrl ?? string.Empty;
            Refresh();
        };

        var fields = new StackPanel { Spacing = 8, Margin = new Thickness(4), Width = 280 };

        fields.Children.Add(Caption("Provider"));
        fields.Children.Add(providerBox);
        fields.Children.Add(Caption("Model"));
        fields.Children.Add(modelBox);
        fields.Children.Add(Caption("Endpoint"));
        fields.Children.Add(baseUrlBox);
        fields.Children.Add(Caption("API key"));
        fields.Children.Add(keyBox);
        fields.Children.Add(keyNote);
        fields.Children.Add(rememberBox);
        fields.Children.Add(visionBox);
        fields.Children.Add(Caption("Effort"));
        fields.Children.Add(effortBox);

        var save = new Button { Content = "Save", Width = 84 };
        save.Click += (_, _) => SaveSettings();

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

    private static TextBlock Caption(string text) =>
        new() { Text = text, FontSize = 11, Foreground = Dim };

    /// <summary>
    /// Enter asks; Ctrl+Enter — and Shift+Enter, which every other message box
    /// in the world accepts — breaks the line.
    /// </summary>
    /// <remarks>
    /// The line break is put in by hand rather than left to the box. Whether a
    /// <see cref="TextBox"/> types a newline for a gesture with a modifier held
    /// is an implementation detail of Avalonia's, and the alternative to knowing
    /// is a message that silently refuses to grow a second line.
    /// </remarks>
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

    private void ReadSettingsIntoControls()
    {
        if (assistant is not null)
        {
            providerBox.SelectedIndex = plugins.Assistants
                .Select((a, i) => (a, i))
                .Where(pair => pair.a.Id == assistant.Id)
                .Select(pair => pair.i)
                .DefaultIfEmpty(-1)
                .First();

            modelBox.Text = settings.Model.Length > 0 ? settings.Model : assistant.Schema.DefaultModel;
            baseUrlBox.Text = settings.BaseUrl ?? assistant.Schema.DefaultBaseUrl ?? string.Empty;
            baseUrlBox.IsEnabled = assistant.Schema.BaseUrlEditable;
        }

        visionBox.IsChecked = settings.Vision;
        rememberBox.IsChecked = settings.RememberKey;
        effortBox.SelectedIndex = (int)settings.Effort;
    }

    private void SaveSettings()
    {
        settings.Model = modelBox.Text ?? string.Empty;
        settings.BaseUrl = string.IsNullOrWhiteSpace(baseUrlBox.Text) ? null : baseUrlBox.Text;
        settings.Vision = visionBox.IsChecked == true;
        settings.RememberKey = rememberBox.IsChecked == true;
        settings.Effort = (AssistantEffort)Math.Max(0, effortBox.SelectedIndex);

        if (assistant is not null)
        {
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

    private AssistantConfig? Configured()
    {
        if (assistant is null) return null;

        var key = credentials.Of(assistant.Id, assistant.Schema.EnvironmentVariable) ?? string.Empty;
        var model = string.IsNullOrWhiteSpace(modelBox.Text) ? assistant.Schema.DefaultModel : modelBox.Text;
        var url = string.IsNullOrWhiteSpace(baseUrlBox.Text) ? assistant.Schema.DefaultBaseUrl : baseUrlBox.Text;

        return new AssistantConfig(
            key,
            model,
            url,
            visionBox.IsChecked == true,
            (AssistantEffort)Math.Max(0, effortBox.SelectedIndex));
    }

    /// <summary>
    /// Reworks what the buttons and the footer say. The footer is the standing
    /// disclosure: it names what leaves the machine and where the key came from,
    /// and it is never hidden behind a dialog nobody reads twice.
    /// </summary>
    private void Refresh()
    {
        var busy = asking;
        var config = Configured();
        var excuse = assistant is null
            ? "No assistant plugin is installed. See the status bar for where plugins are looked for."
            : config is null ? null : Excuse(assistant, config);

        stop.IsEnabled = busy;
        accept.IsEnabled = !busy && run?.Proposal is not null;
        revert.IsEnabled = !busy && before is not null;

        // On the box, since the box is what sending is done from now. The footer
        // says the same thing in amber a few pixels below, so nothing is lost to
        // anybody who never hovers.
        ToolTip.SetTip(instruction, excuse);
        ShowKeyState();

        if (excuse is not null)
        {
            footer.Text = excuse;
            footer.Foreground = Amber;
            return;
        }

        var source = credentials.SourceOf(assistant!.Id, assistant.Schema.EnvironmentVariable) switch
        {
            CredentialSource.Environment => $"key from {assistant.Schema.EnvironmentVariable}",
            CredentialSource.Kept => $"key kept by {credentials.Store?.Name}",
            CredentialSource.Session => credentials.CanKeep
                ? "key held for this session only"
                : "key held for this session only — nothing installed can keep one",
            _ => "no key",
        };

        footer.Foreground = Dim;
        footer.Text =
            $"Sends your instruction, the module list and the patch — including rendered frames of it — "
            + $"to {assistant.Name}. {source}.";
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
        beacon.Opacity = 0.25d + (0.75d * ((Math.Cos(pulse * Math.PI / 5d) + 1d) / 2d));

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

        var variable = assistant.Schema.EnvironmentVariable;
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
                $"In force, from {variable}. Flyback never wrote it and never will. A key entered here "
                + "takes precedence over it, and forgetting that one comes back to this.",

            _ => assistant.Schema.CredentialHelp,
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

        var source = credentials.SourceOf(assistant.Id, assistant.Schema.EnvironmentVariable);

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

    private async Task AskAsync()
    {
        if (asking || assistant is null) return;
        if (Configured() is not { } config || Excuse(assistant, config) is not null) return;

        var wanted = instruction.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(wanted)) return;

        said.Children.Clear();
        lastFrame.Source = null;
        lastFrame.IsVisible = false;
        warnedAboutEdits = false;

        Add(wanted, Brushes.White, 12);

        run?.Dispose();
        run = new AssistantRun(assistant, config, plugins.Modules, current());

        instruction.Text = string.Empty;

        StartWorking();
        Refresh();

        try
        {
            await foreach (var happened in run.Ask(wanted))
            {
                Show(happened);

                // The tallies move as the workbench is driven, and an edit that
                // lands is the strongest sign of all that this is alive.
                Beat();
            }
        }
        catch (Exception ex)
        {
            // AssistantRun already turns a provider's failure into an event, so
            // anything arriving here is the shell's own fault rather than a
            // plugin's — but the window still survives it.
            Add($"Something went wrong: {ex.Message}", Amber, 11);
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
                break;

            case PatchEvent.Did did:
                Add(did.Summary, Dim, 11);
                break;

            case PatchEvent.Saw saw:
                Add(saw.Caption, Dim, 11);
                Picture(saw.Png);
                break;

            case PatchEvent.Cost cost:
                Add(
                    $"{cost.Input} in ({cost.CacheRead} cached), {cost.Output} out.",
                    Dim,
                    11);
                break;

            case PatchEvent.Proposed proposed:
                Add($"Proposed: {proposed.Summary}", Brushes.White, 12);
                break;

            case PatchEvent.Failed failed:
                Add(failed.Message, Amber, 11);
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

    private void Add(string text, IBrush colour, double size)
    {
        said.Children.Add(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = colour,
            FontSize = size,
        });
    }

    /// <summary>Streamed prose arrives in pieces, so it lands on the end of the last one.</summary>
    private void Append(string text)
    {
        if (said.Children.Count > 0 && said.Children[^1] is SelectableTextBlock last && last.FontSize > 11.5)
        {
            last.Text += text;
            return;
        }

        Add(text, Brushes.White, 12);
    }

    // --- accepting ----------------------------------------------------------

    private async Task Accept()
    {
        if (run?.Proposal is not { } proposed) return;

        // Applying would throw away whatever they did in the meantime, so it
        // takes two clicks once and one thereafter.
        if (run.EditedUnderneath(current()) && !warnedAboutEdits)
        {
            warnedAboutEdits = true;
            report("You edited the patch while this ran — applying will replace your edits. Click Apply again to go ahead.", null);
            return;
        }

        // Read before the canvas is handed anything, and kept only once it
        // took it: a proposal that was refused is not one there is anything to
        // revert to.
        var previous = current();

        if (!await apply(proposed)) return;

        before = previous;
        report(string.Empty, null);
        Refresh();
    }

    private async Task Revert()
    {
        if (before is not { } previous) return;
        if (!await apply(previous)) return;

        before = null;
        Refresh();
    }
}
