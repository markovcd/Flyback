using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    /// Scanned once, before anything else exists. Nothing in the shell knows
    /// which backends there are or what they are called.
    /// </summary>
    private readonly PluginCatalog plugins = PluginHost.Load();

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
        var presets = new ComboBox
        {
            ItemsSource = Presets.All.Select(p => p.Name).ToList(),
            SelectedIndex = 0,
            Width = 160,
        };
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex >= 0 && presets.SelectedIndex < Presets.All.Count)
            {
                editor.Patch = Presets.All[presets.SelectedIndex].Build();
                preview.Rewind();
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
        var list = new StackPanel { Margin = new Thickness(10, 8), Spacing = 2 };

        list.Children.Add(new TextBlock
        {
            Text = "Click to drop a module into the middle of the patch.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.55,
            Margin = new Thickness(2, 0, 2, 8),
        });

        foreach (var category in NodeCatalog.Categories)
        {
            list.Children.Add(new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(NodeGeometry.Accent(category)),
                Margin = new Thickness(2, 12, 2, 4),
            });

            foreach (var def in NodeCatalog.All.Where(d => d.Category == category))
            {
                var button = new Button
                {
                    Content = def.Name,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4),
                    FontSize = 12,
                };

                if (!string.IsNullOrEmpty(def.Description))
                    ToolTip.SetTip(button, def.Description);

                var typeId = def.TypeId;
                button.Click += (_, _) => editor.AddNode(typeId);
                list.Children.Add(button);
            }
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x22)),
            Child = new ScrollViewer { Content = list },
        };
    }

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

        var inspectorBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1E, 0x22)),
            Child = new ScrollViewer { Content = inspector },
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
        var lines = new List<string>();

        lines.Add(plugins.Plugins.Count == 0 ? "No plugins loaded." : "Loaded:");
        lines.AddRange(plugins.Plugins.Select(p => $"    {p.Info.Name}  ({p.Info.Id})"));

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

        issues.Text = result.HasIssues
            ? string.Join("  •  ", result.Issues.Select(i => i.Message))
            : string.Empty;
    }

    private void SetAudioEnabled(bool enabled)
    {
        audioButton.Content = enabled ? "Audio on" : "Audio off";

        if (enabled)
        {
            audio.Update(editor.Patch);
            audio.Start();

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
            editor.Patch = PatchIo.FromJson(await reader.ReadToEndAsync());
            preview.Rewind();
        }
        catch (Exception ex)
        {
            issues.Text = $"Could not open patch: {ex.Message}";
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
            issues.Text = $"Could not save patch: {ex.Message}";
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
                issues.Text = "That location can't be written to directly.";
                return;
            }

            await PreviewSurface.SaveFrameAsync(preview.Program, preview.Time, path, ExportSize);
        }
        catch (Exception ex)
        {
            issues.Text = $"Could not save frame: {ex.Message}";
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
                issues.Text = "That location can't be written to directly.";
                return;
            }

            var patch = editor.Patch;
            await Task.Run(() => RenderAudioFile(patch, path));
        }
        catch (Exception ex)
        {
            issues.Text = $"Could not render audio: {ex.Message}";
        }
    }

    /// <summary>
    /// Renders offline through a fresh renderer, so exporting never disturbs the
    /// cursor or filter state of whatever is currently playing.
    /// </summary>
    private static void RenderAudioFile(Patch patch, string path)
    {
        const int sampleRate = 48_000;

        var program = patch.CompileForAudio().Program;

        var buffer = new float[sampleRate * ExportSeconds * NodeCatalog.AudioChannels];
        new AudioRenderer(sampleRate).Render(program, buffer, AudioScanFor(patch));

        WavWriter.Write(path, buffer, sampleRate, NodeCatalog.AudioChannels);
    }

    private static AudioScan AudioScanFor(Patch patch)
    {
        var sink = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.AudioOutputTypeId);
        var def = NodeCatalog.Get(NodeCatalog.AudioOutputTypeId);
        if (sink is null || def is null) return AudioScan.TimeDriven;

        float Knob(string name)
        {
            for (var i = 0; i < def.Inputs.Count; i++)
                if (def.Inputs[i].Name == name)
                    return i < sink.InputValues.Length ? sink.InputValues[i] : def.Inputs[i].Default;
            return 0f;
        }

        return new AudioScan(Knob("scan") >= 0.5f, MathF.Max(Knob("scan rate"), 1f), 16f / 9f);
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
