using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
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
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x99));
    private static readonly IBrush Amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40));

    /// <summary>The middle of <see cref="LogoMark"/>'s sweep, borrowed for the one thing here that is alive.</summary>
    private static readonly IBrush Live = new SolidColorBrush(Color.FromRgb(0x3F, 0xC8, 0xC8));

    private readonly PluginCatalog plugins;
    private readonly Func<Patch> current;
    private readonly Action<Patch> apply;
    private readonly Action<string, string?> report;

    private readonly AssistantSettings settings = AssistantSettings.Load();
    private readonly Credentials credentials;

    private readonly TextBox instruction = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Describe the patch you want. Ctrl+Enter to ask.",
        FontSize = 12,
        MinHeight = 64,
    };

    private readonly StackPanel said = new() { Spacing = 4, Margin = new Thickness(10, 8) };
    private readonly ScrollViewer transcript = new();
    private readonly Image lastFrame = new() { Width = 160, Stretch = Stretch.Uniform };
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

    private readonly Button ask = new() { Content = "Ask", Width = 96 };
    private readonly Button stop = new() { Content = "Stop", Width = 96, IsEnabled = false };
    private readonly Button accept = new() { Content = "Apply", Width = 96, IsEnabled = false };
    private readonly Button revert = new() { Content = "Put it back", Width = 96, IsEnabled = false };

    private readonly TextBox keyBox = new() { PasswordChar = '•', FontSize = 12, Width = 260 };
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
    /// its buttons. Asking the run left Ask live and Stop dead for the whole of
    /// every turn. This is set the moment somebody clicks, which is the moment
    /// it becomes true from out here.
    /// </remarks>
    private bool asking;

    private bool stopping;
    private DateTime startedAt;
    private int pulse;

    public AssistantPanel(
        PluginCatalog plugins,
        Func<Patch> current,
        Action<Patch> apply,
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

        instruction.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

            e.Handled = true;
            _ = AskAsync();
        };

        ask.Click += async (_, _) => await AskAsync();

        stop.Click += (_, _) =>
        {
            // Cancellation lands at the next thing the assistant does, which may
            // be a whole request away. Saying so beats a button that goes dead
            // and a panel that carries on as if nothing was asked of it.
            stopping = true;
            run?.Stop();
            Beat();
        };

        accept.Click += (_, _) => Accept();
        revert.Click += (_, _) => Revert();

        var settingsButton = new Button { Content = "Settings", Width = 96 };
        settingsButton.Flyout = new Flyout
        {
            Content = BuildSettings(),
            Placement = PlacementMode.TopEdgeAlignedRight,
        };

        var buttons = new StackPanel { Spacing = 6, Width = 96 };
        buttons.Children.Add(ask);
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

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Height = 240,
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
        fields.Children.Add(rememberBox);
        fields.Children.Add(visionBox);
        fields.Children.Add(Caption("Effort"));
        fields.Children.Add(effortBox);

        var save = new Button { Content = "Save", Width = 84 };
        save.Click += (_, _) => SaveSettings();

        var forget = new Button { Content = "Forget key", Width = 100 };
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

        if (assistant is not null && !string.IsNullOrWhiteSpace(keyBox.Text))
            credentials.Accept(assistant.Id, keyBox.Text, settings.RememberKey && credentials.CanKeep);

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

        ask.IsEnabled = !busy && excuse is null;
        stop.IsEnabled = busy;
        accept.IsEnabled = !busy && run?.Proposal is not null;
        revert.IsEnabled = !busy && before is not null;

        ToolTip.SetTip(ask, excuse);

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
        }
        catch
        {
            // A frame that will not decode is not worth the window. The caption
            // that came with it is already in the transcript.
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

    private void Accept()
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

        before = current();
        apply(proposed);
        report(string.Empty, null);
        Refresh();
    }

    private void Revert()
    {
        if (before is not { } previous) return;

        apply(previous);
        before = null;
        Refresh();
    }
}
