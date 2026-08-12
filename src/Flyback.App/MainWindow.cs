using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

public sealed class MainWindow : Window
{
    private static readonly (string Label, PixelSize Size)[] Resolutions =
    [
        ("320 x 180", new PixelSize(320, 180)),
        ("480 x 270", new PixelSize(480, 270)),
        ("640 x 360", new PixelSize(640, 360)),
        ("960 x 540", new PixelSize(960, 540)),
        ("1280 x 720", new PixelSize(1280, 720)),
        ("1920 x 1080", new PixelSize(1920, 1080)),
    ];

    private static readonly PixelSize ExportSize = new(1920, 1080);

    private const int ExportSeconds = 10;

    private readonly NodeEditor editor = new();
    private readonly PreviewSurface preview = new();

    /// <summary>
    /// Behind the inspector, and brighter when there is nothing selected for it
    /// to sit behind. Never hit-testable, so it cannot swallow a click meant for
    /// a slider underneath.
    /// </summary>
    private readonly LogoMark watermark = new()
    {
        // Fills the panel and centres itself, so it grows with the splitter
        // instead of being sized for one particular panel width.
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(20),

        // The no-selection value, so it is never briefly full strength if
        // something ever builds the panel before the first selection lands.
        Opacity = 0.14,
        IsHitTestVisible = false,
    };

    private readonly StackPanel modules = new() { Margin = new Thickness(10, 0, 10, 8), Spacing = 2 };
    private readonly TextBox filter = new()
    {
        PlaceholderText = "Filter modules",
        FontSize = 12,
    };

    /// <summary>
    /// Opens the list of plugins to show modules from. The engine's own are one
    /// entry among the rest, because that is exactly what the catalogue thinks
    /// they are.
    /// </summary>
    private readonly Button sources = new()
    {
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(8, 4),
    };

    /// <summary>
    /// Providers whose modules are hidden. Stored as what is *off* rather than
    /// what is on, so a plugin installed later shows up without having to be
    /// found and ticked.
    /// </summary>
    private readonly HashSet<string> hidden = [];

    private readonly StackPanel inspector = new() { Margin = new Thickness(12), Spacing = 8 };
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock issues = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40)),
    };

    private readonly TextBlock backend = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12,
        Opacity = 0.55,
    };

    private readonly ToggleButton playButton = new() { Content = "Pause", Width = 78, IsChecked = true };
    private readonly ToggleButton audioButton = new() { Content = "Audio off", Width = 92 };

    /// <summary>
    /// Read before this window existed, and already installed. Nothing here
    /// knows which backends or modules there are, or what they are called.
    /// </summary>
    private readonly PluginCatalog plugins = Startup.Plugins;

    private readonly AudioSetup sound;
    private readonly AudioEngine audio;

    public MainWindow()
    {
        sound = OpenAudio(plugins);
        audio = new AudioEngine(sound.Device);

        Title = "Flyback — patchable video synthesiser";
        Width = 1280;
        Height = 800;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x18, 0x1B));

        editor.PatchChanged += (_, _) => Recompile();
        editor.SelectionChanged += (_, _) => BuildInspector();

        Content = BuildLayout();

        editor.Patch = Presets.Default();

        var ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        ticker.Tick += (_, _) => UpdateStatus();
        ticker.Start();
    }

    // --- layout ---------------------------------------------------------------

    private Control BuildLayout()
    {
        var root = new DockPanel();

        var toolbar = BuildToolbar();
        var statusBar = BuildStatusBar();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);

        // The two flexible columns are star-sized: GridSplitter redistributes
        // star weights, and a fixed-pixel column next to one just gets squeezed.
        var columns = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(220, GridUnitType.Pixel) { MinWidth = 150 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)) { MinWidth = 280 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)) { MinWidth = 300 },
            ],
        };

        var palette = BuildPalette();
        Grid.SetColumn(palette, 0);
        Grid.SetColumn(editor, 2);

        var leftSplitter = new GridSplitter { Width = 5, Background = Brushes.Transparent };
        Grid.SetColumn(leftSplitter, 1);

        var rightSplitter = new GridSplitter { Width = 5, Background = Brushes.Transparent };
        Grid.SetColumn(rightSplitter, 3);

        var right = BuildRightPanel();
        Grid.SetColumn(right, 4);

        columns.Children.Add(palette);
        columns.Children.Add(leftSplitter);
        columns.Children.Add(editor);
        columns.Children.Add(rightSplitter);
        columns.Children.Add(right);

        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(columns);

        return root;
    }

    private Control BuildToolbar()
    {
        // Plugin presets sit after the engine's own, so the list the app opens
        // on is the same wherever it is installed.
        var available = plugins.Presets;

        var presets = new ComboBox
        {
            ItemsSource = available.Select(p => p.Name).ToList(),
            SelectedIndex = 0,
            Width = 160,
        };
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex < 0 || presets.SelectedIndex >= available.Count) return;

            var preset = available[presets.SelectedIndex];

            try
            {
                // A preset from a plugin is built here, not when it was
                // registered, so this is where a plugin that offered a patch
                // using modules it failed to add finally shows up.
                editor.Patch = preset.Build(plugins.Modules);
                preview.Rewind();
            }
            catch (Exception ex)
            {
                Report($"Could not build the '{preset.Name}' preset: {ex.Message}");
            }
        };

        var open = new Button { Content = "Open…" };
        open.Click += async (_, _) => await OpenPatchAsync();

        var save = new Button { Content = "Save…" };
        save.Click += async (_, _) => await SavePatchAsync();

        playButton.IsCheckedChanged += (_, _) =>
        {
            preview.IsPlaying = playButton.IsChecked == true;
            playButton.Content = preview.IsPlaying ? "Pause" : "Play";
        };

        var rewind = new Button { Content = "Rewind" };
        rewind.Click += (_, _) =>
        {
            audio.Rewind();
            preview.Rewind();
        };

        // Off by default: launching a program should not make a noise. And it
        // cannot be switched on at all where no plugin offered a device.
        audioButton.IsEnabled = sound.Output is not null;
        ToolTip.SetTip(audioButton, sound.Output is { } output
            ? $"Play the patch through {output.Name}. Needs an Audio Output module."
            : "No sound backend is installed. See the status bar for where plugins are looked for.");
        audioButton.IsCheckedChanged += (_, _) => SetAudioEnabled(audioButton.IsChecked == true);

        var exportAudio = new Button { Content = "Render audio…" };
        ToolTip.SetTip(exportAudio, $"Render {ExportSeconds} seconds of the patch to a WAV file.");
        exportAudio.Click += async (_, _) => await SaveAudioAsync();

        var resolution = new ComboBox
        {
            ItemsSource = Resolutions.Select(r => r.Label).ToList(),
            SelectedIndex = 3,
            Width = 130,
        };
        resolution.SelectionChanged += (_, _) =>
        {
            if (resolution.SelectedIndex >= 0)
                preview.Resolution = Resolutions[resolution.SelectedIndex].Size;
        };
        preview.Resolution = Resolutions[3].Size;

        var exportFrame = new Button { Content = "Save frame…" };
        ToolTip.SetTip(exportFrame, $"Render the current moment at {ExportSize.Width} x {ExportSize.Height} and write a PNG.");
        exportFrame.Click += async (_, _) => await SaveFrameAsync();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };

        bar.Children.Add(Label("Patch"));
        bar.Children.Add(presets);
        bar.Children.Add(open);
        bar.Children.Add(save);
        bar.Children.Add(Separator());
        bar.Children.Add(playButton);
        bar.Children.Add(rewind);
        bar.Children.Add(Separator());
        bar.Children.Add(Label("Preview"));
        bar.Children.Add(resolution);
        bar.Children.Add(exportFrame);
        bar.Children.Add(Separator());
        bar.Children.Add(audioButton);
        bar.Children.Add(exportAudio);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private Control BuildStatusBar()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(12, 5),
        };

        backend.Text = sound.Output is { } output ? $"sound: {output.Name}" : "sound: none";
        ToolTip.SetTip(backend, PluginSummary());

        bar.Children.Add(status);
        bar.Children.Add(backend);
        bar.Children.Add(issues);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        };
    }

    private Control BuildPalette()
    {
        filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) FillPalette();
        };

        // Escape empties it, which is the quickest way back to the whole list
        // once you have found the module you were after.
        filter.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            filter.Text = string.Empty;
            e.Handled = true;
        };

        var ticks = new StackPanel { Spacing = 2, Margin = new Thickness(4) };

        foreach (var provider in Providers)
        {
            var id = provider.Id;
            var tick = new CheckBox { Content = provider.Name, IsChecked = true, FontSize = 12 };

            tick.IsCheckedChanged += (_, _) =>
            {
                if (tick.IsChecked == true) hidden.Remove(id); else hidden.Add(id);

                DescribeSources();
                FillPalette();
            };

            ticks.Children.Add(tick);
        }

        sources.Flyout = new Flyout { Content = ticks, Placement = PlacementMode.BottomEdgeAlignedLeft };

        DescribeSources();
        FillPalette();

        var header = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8, 10, 0) };
        header.Children.Add(filter);

        // With nothing installed there is only the engine's own entry, and a
        // dropdown that can only say "all" or "the only one" is noise.
        if (Providers.Count > 1) header.Children.Add(sources);

        // The header stays put while the list beneath it scrolls; a filter that
        // scrolled away with the results would be the wrong way round.
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);

        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer { Content = modules });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x22)),
            Child = panel,
        };
    }

    /// <summary>
    /// Rebuilds the module list for whatever is in the filter box. Rebuilding
    /// rather than hiding buttons keeps the category headings honest: a heading
    /// with nothing under it is worse than no heading.
    /// </summary>
    private void FillPalette()
    {
        var text = filter.Text?.Trim() ?? string.Empty;
        var matches = NodeCatalog.All.Where(d => Matches(d, text)).ToList();

        modules.Children.Clear();

        if (matches.Count == 0)
        {
            modules.Children.Add(Hint(
                hidden.Count == Providers.Count ? "No plugins are ticked."
                : text.Length == 0 ? "Nothing to show."
                : $"Nothing matches “{text}”."));
            return;
        }

        if (text.Length == 0 && hidden.Count == 0)
            modules.Children.Add(Hint("Click to drop a module into the middle of the patch."));

        foreach (var category in matches.Select(d => d.Category).Distinct())
        {
            modules.Children.Add(new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(NodeGeometry.Accent(category)),
                Margin = new Thickness(2, 12, 2, 4),
            });

            foreach (var def in matches.Where(d => d.Category == category))
            {
                var button = new Button
                {
                    Content = def.Name,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4),
                    FontSize = 12,
                };

                // Naming the plugin only where it is not the engine keeps the
                // built-in modules reading as they always did, and tells you
                // which patches will need something installed to open.
                var from = plugins.Modules.ProviderOf(def.TypeId);
                var origin = from is null || from.Id == NodeCatalog.BuiltInProvider.Id
                    ? string.Empty
                    : $"{Environment.NewLine}{Environment.NewLine}From {from.Name} ({from.Id})";

                if (def.Description.Length + origin.Length > 0)
                    ToolTip.SetTip(button, def.Description + origin);

                var typeId = def.TypeId;
                button.Click += (_, _) => editor.AddNode(typeId);
                modules.Children.Add(button);
            }
        }
    }

    /// <summary>Every provider the installed catalogue knows, the engine's own first.</summary>
    private IReadOnlyList<ModuleProvider> Providers => plugins.Modules.Providers;

    /// <summary>Says what the ticks add up to, so the state is readable without opening them.</summary>
    private void DescribeSources()
    {
        var showing = Providers.Count - hidden.Count;

        sources.Content = showing switch
        {
            _ when hidden.Count == 0 => "All modules  ▾",
            0 => "No plugins  ▾",
            1 => $"{Providers.First(p => !hidden.Contains(p.Id)).Name}  ▾",
            _ => $"{showing} of {Providers.Count} plugins  ▾",
        };
    }

    /// <summary>
    /// Text matches name, category and type id, but deliberately not the
    /// description: every module has a sentence of prose, and matching it turns a
    /// search for a common word into most of the catalogue. The ticks narrow
    /// separately, so the two combine rather than compete.
    /// </summary>
    private bool Matches(NodeDef def, string text)
    {
        if (plugins.Modules.ProviderOf(def.TypeId) is { } from && hidden.Contains(from.Id)) return false;

        return text.Length == 0
            || def.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
            || def.TypeId.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Opacity = 0.55,
        Margin = new Thickness(2, 8, 2, 0),
    };

    private Control BuildRightPanel()
    {
        var grid = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 140 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1.1, GridUnitType.Star)) { MinHeight = 120 },
            ],
        };

        var previewBox = new Border
        {
            Background = Brushes.Black,
            Child = preview,
        };
        Grid.SetRow(previewBox, 0);

        var splitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
        Grid.SetRow(splitter, 1);

        // The mark sits behind the inspector rather than beside it, and never
        // takes a click — an empty panel is a better place for it than a corner
        // of the toolbar, and it is out of the way once there is something to read.
        var inspectorBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x22)),
            Child = new Panel
            {
                Children =
                {
                    watermark,

                    // Explicitly transparent: a theme that gave the scroll
                    // viewer a background would paint straight over the mark.
                    new ScrollViewer { Content = inspector, Background = Brushes.Transparent },
                },
            },
        };
        Grid.SetRow(inspectorBorder, 2);

        grid.Children.Add(previewBox);
        grid.Children.Add(splitter);
        grid.Children.Add(inspectorBorder);

        return grid;
    }

    // --- inspector -------------------------------------------------------------

    /// <summary>
    /// Rebuilt whenever selection changes. The canvas handles patching; exact
    /// numbers are easier to set with real controls than by dragging on a knob.
    /// </summary>
    private void BuildInspector()
    {
        inspector.Children.Clear();

        var selected = editor.SelectedNode is { } chosen && NodeCatalog.Get(chosen.TypeId) is not null;

        // Louder with nothing in front of it, faint once there are values to
        // read. A watermark that competed with a column of sliders would be a
        // decoration in the way of the thing it decorates.
        watermark.Opacity = selected ? 0.06 : 0.14;

        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            inspector.Children.Add(new TextBlock
            {
                Text = "Select a module to edit its values.\n\n"
                     + "Drag from a socket to patch it into another.\n"
                     + "Drag a connected input to unplug it.\n"
                     + "Right-drag or drag the background to pan, wheel to zoom.\n"
                     + "Delete removes the selected module, F frames the patch.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                FontSize = 12,
            });
            return;
        }

        inspector.Children.Add(new TextBlock
        {
            Text = def.Name,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });

        inspector.Children.Add(new TextBlock
        {
            Text = def.Category,
            FontSize = 11,
            Foreground = new SolidColorBrush(NodeGeometry.Accent(def.Category)),
        });

        if (!string.IsNullOrEmpty(def.Description))
            inspector.Children.Add(new TextBlock
            {
                Text = def.Description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 6),
            });

        for (var i = 0; i < def.Inputs.Count; i++)
            inspector.Children.Add(BuildInputRow(node, def.Inputs[i], i));

        if (def.Inputs.Count == 0)
            inspector.Children.Add(new TextBlock
            {
                Text = "This module has nothing to set — it only produces.",
                Opacity = 0.5,
                FontSize = 12,
            });

        var delete = new Button
        {
            Content = "Delete module",
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        delete.Click += (_, _) => editor.DeleteSelected();
        inspector.Children.Add(delete);
    }

    private Control BuildInputRow(NodeInstance node, PortSpec spec, int index)
    {
        var connected = editor.Patch.IncomingTo(node.Id, index) is not null;

        var label = new TextBlock
        {
            Text = spec.Name,
            Width = 78,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*,84") };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        if (connected)
        {
            var wired = new TextBlock
            {
                Text = "◀ patched",
                FontSize = 12,
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(wired, 1);
            Grid.SetColumnSpan(wired, 2);
            row.Children.Add(wired);
            return row;
        }

        var value = index < node.InputValues.Length ? node.InputValues[index] : spec.Default;

        var slider = new Slider
        {
            // Widen the range if a saved value sits outside the module's usual span.
            Minimum = Math.Min(spec.Min, value),
            Maximum = Math.Max(spec.Max, value),
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
        };

        var numeric = new NumericUpDown
        {
            Value = (decimal)value,
            Increment = 0.05m,
            FormatString = "0.###",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            ShowButtonSpinner = false,
        };

        var updating = false;

        void Apply(float next)
        {
            if (updating) return;

            updating = true;
            if (index < node.InputValues.Length) node.InputValues[index] = next;
            slider.Value = next;
            numeric.Value = (decimal)next;
            updating = false;

            editor.NotifyPatchChanged();
        }

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && e.NewValue is double d)
                Apply((float)d);
        };

        numeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } d) Apply((float)d);
        };

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(numeric, 2);
        row.Children.Add(slider);
        row.Children.Add(numeric);

        return row;
    }

    // --- plugins -----------------------------------------------------------------

    /// <summary>The device that was opened, and what it came from — null when nothing could play.</summary>
    private sealed record AudioSetup(IAudioDevice Device, IAudioOutput? Output, string? Failure);

    /// <summary>
    /// Opens the best backend the plugins offered. A machine with no sound
    /// plugin, or one whose device refuses to open, gets silence and a disabled
    /// button — never a program that will not start.
    /// </summary>
    private static AudioSetup OpenAudio(PluginCatalog plugins)
    {
        if (plugins.PreferredAudioOutput is not { } output)
            return new AudioSetup(new SilentAudioDevice(), null, null);

        try
        {
            return new AudioSetup(output.Create(AudioFormat.Default), output, null);
        }
        catch (Exception ex)
        {
            return new AudioSetup(new SilentAudioDevice(), null, $"{output.Name} — {ex.Message}");
        }
    }

    /// <summary>
    /// What loaded and what did not. This is the only place a plugin failure is
    /// reported, so it says where the folder is even when it is empty.
    /// </summary>
    private string PluginSummary()
    {
        var lines = new List<string> { plugins.Plugins.Count == 0 ? "No plugins loaded." : "Loaded:" };

        lines.AddRange(plugins.Plugins.Select(p => $"    {p.Info.Name}  ({p.Info.Id})"));

        // Module providers are worth naming separately: they are what a saved
        // patch records, and what another machine would have to install.
        var providers = plugins.Modules.Providers.Where(p => p.Id != NodeCatalog.BuiltInProvider.Id).ToList();

        if (providers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Modules from:");
            lines.AddRange(providers.Select(p => $"    {p.Name}  ({p.Id})"));
        }

        if (sound.Failure is { } failure)
            lines.Add($"Could not open sound: {failure}");

        lines.AddRange(plugins.Problems.Select(p => $"Problem: {p}"));

        lines.Add(string.Empty);
        lines.Add(PluginHost.DefaultDirectory);

        return string.Join(Environment.NewLine, lines);
    }

    // --- compile and status -----------------------------------------------------

    /// <summary>
    /// One patch, one program per sink. The audio program is compiled even when
    /// sound is off, so switching it on is instant and the status line can show
    /// what the ear would cost.
    /// </summary>
    private void Recompile()
    {
        var result = editor.Patch.CompileForVideo();
        preview.Program = result.Program;
        audio.Update(editor.Patch);

        Report(result.HasIssues
            ? string.Join("  •  ", result.Issues.Select(i => i.Message))
            : string.Empty);
    }

    /// <summary>
    /// The one place anything is said to the user. <paramref name="detail"/> is
    /// for what will not fit on a status bar — a list of missing plugins, say —
    /// and is cleared along with the line, so nothing stale hangs off it.
    /// </summary>
    private void Report(string message, string? detail = null)
    {
        issues.Text = message;
        ToolTip.SetTip(issues, string.IsNullOrEmpty(detail) ? null : detail);
    }

    private void SetAudioEnabled(bool enabled)
    {
        audioButton.Content = enabled ? "Audio on" : "Audio off";

        if (enabled)
        {
            audio.Update(editor.Patch);

            try
            {
                audio.Start();
            }
            catch (Exception ex)
            {
                // A device is only really opened here, so this is where a card
                // that is busy, unplugged or missing its library says so. The
                // button goes back off and stays off: whatever it is will not
                // have fixed itself by the next click, and ADR-0025 promises
                // that nothing a plugin does takes the shell down.
                Report($"Sound could not start — {ex.Message}", PluginSummary());
                audioButton.IsEnabled = false;
                audioButton.IsChecked = false;
                return;
            }

            // Sound cannot stretch, so it leads and the picture follows.
            preview.Clock = () => audio.Time;
        }
        else
        {
            preview.Clock = null;
            audio.Stop();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        audio.Dispose();
        base.OnClosed(e);
    }

    private void UpdateStatus()
    {
        var nodes = editor.Patch.Nodes.Count;
        var wires = editor.Patch.Connections.Count;
        var ops = preview.Program.Ops.Length;
        var ms = preview.FrameMilliseconds;
        var size = preview.Resolution;

        // The loop is capped at ~60 Hz, so report the cost of a frame rather
        // than a frame rate the timer would never let you observe.
        status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{nodes} modules · {wires} wires · {ops} ops   |   t = {preview.Time:0.00}s   |   {ms:0.0} ms to render {size.Width} × {size.Height}");
    }

    // --- files -------------------------------------------------------------------

    private async Task OpenPatchAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch",
            AllowMultiple = false,
            FileTypeFilter = [PatchFileType],
        });

        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var loaded = PatchIo.Read(await reader.ReadToEndAsync());

            // A patch short of a module would open with holes in it and compile
            // to something that is not what was saved. Better to refuse it and
            // say what is missing, leaving what is open where it was.
            if (!loaded.IsComplete)
            {
                Report($"Not opened. {loaded.Summary}", loaded.Detail);
                return;
            }

            editor.Patch = loaded.Patch;
            preview.Rewind();
        }
        catch (Exception ex)
        {
            Report($"Could not open patch: {ex.Message}");
        }
    }

    private async Task SavePatchAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save patch",
            SuggestedFileName = "patch",
            DefaultExtension = PatchIo.FileExtension,
            FileTypeChoices = [PatchFileType],
        });

        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(PatchIo.ToJson(editor.Patch));
        }
        catch (Exception ex)
        {
            Report($"Could not save patch: {ex.Message}");
        }
    }

    private async Task SaveFrameAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save frame",
            SuggestedFileName = "frame",
            DefaultExtension = "png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
        });

        if (file is null) return;

        try
        {
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                Report("That location can't be written to directly.");
                return;
            }

            await PreviewSurface.SaveFrameAsync(preview.Program, preview.Time, path, ExportSize);
        }
        catch (Exception ex)
        {
            Report($"Could not save frame: {ex.Message}");
        }
    }

    private async Task SaveAudioAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Render audio",
            SuggestedFileName = "flyback",
            DefaultExtension = "wav",
            FileTypeChoices = [new FilePickerFileType("WAV audio") { Patterns = ["*.wav"] }],
        });

        if (file is null) return;

        try
        {
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                Report("That location can't be written to directly.");
                return;
            }

            var patch = editor.Patch;
            await Task.Run(() => RenderAudioFile(patch, path));
        }
        catch (Exception ex)
        {
            Report($"Could not render audio: {ex.Message}");
        }
    }

    /// <summary>
    /// Renders offline through a fresh renderer, so exporting never disturbs the
    /// cursor or filter state of whatever is currently playing.
    /// </summary>
    private static void RenderAudioFile(Patch patch, string path)
    {
        var program = patch.CompileForAudio().Program;
        var renderer = new AudioRenderer();
        var buffer = new float[renderer.SampleRate * ExportSeconds * NodeCatalog.AudioChannels];
        renderer.Render(program, buffer, AudioScanFor(patch));

        WavWriter.Write(path, buffer, renderer.SampleRate, NodeCatalog.AudioChannels);
    }

    private static AudioScan AudioScanFor(Patch patch)
    {
        var sink = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.AudioOutputTypeId);
        var def = NodeCatalog.Get(NodeCatalog.AudioOutputTypeId);
        if (sink is null || def is null) return AudioScan.TimeDriven;

        return new AudioScan(Knob("scan") >= 0.5f, MathF.Max(Knob("scan rate"), 1f), 16f / 9f);

        float Knob(string name)
        {
            for (var i = 0; i < def.Inputs.Count; i++)
                if (def.Inputs[i].Name == name)
                    return i < sink.InputValues.Length ? sink.InputValues[i] : def.Inputs[i].Default;
            return 0f;
        }
    }

    private static FilePickerFileType PatchFileType => new("Flyback patch")
    {
        Patterns = [$"*.{PatchIo.FileExtension}"],
    };

    // --- small helpers -------------------------------------------------------------

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Opacity = 0.6,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 4),
        Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x46)),
    };
}
