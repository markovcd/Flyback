using Flyback.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Canvas;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Tests.Settings;

public partial class OutputSettingsTests
{
    // --- the recording and sound sections ------------------------------------

    private static ComboBox FrameRate(Visual within) => All<ComboBox>(within).Single(c => c.Name == "frameRate");

    private static NumericUpDown Quality(Visual within) => All<NumericUpDown>(within).Single(c => c.Name == "jpegQuality");

    private static ComboBox Latency(Visual within) => All<ComboBox>(within).Single(c => c.Name == "latency");

    private static ComboBox CountIn(Visual within) => All<ComboBox>(within).Single(c => c.Name == "countIn");

    private static CheckBox RewindFirst(Visual within) =>
        All<CheckBox>(within).Single(c => c.Name == "rewindBeforeTake");

    /// <summary>
    /// Both device tabs say where the device comes from, because everything
    /// either one configures is a plugin's, and neither tab otherwise names one.
    /// Nothing is loaded here, so what they say is the other half: that nothing
    /// plays and nothing is heard, rather than a tab of rows about nothing.
    /// </summary>
    [AvaloniaFact]
    public void The_device_tabs_say_where_the_device_comes_from()
    {
        var window = Open();
        var dialog = OpenSettings(window, SoundTab);

        Named<TextBlock>(dialog, "soundNote").Text
            .ShouldBe("No sound plugin is installed, so nothing plays. See About for where plugins are looked for.");

        ShowSettingsTab(dialog, MidiTab);
        Settle(window);

        Named<TextBlock>(dialog, "midiNote").Text
            .ShouldBe("No MIDI plugin is installed, so the only instrument is the computer's own keyboard.");
    }

    /// <summary>
    /// With one installed, the sentence names the backend and then the plugin
    /// behind it — the plugin by both the name About lists it under and the id
    /// its folder goes by, so it can be found and taken away again.
    /// </summary>
    [AvaloniaFact]
    public void A_backend_is_named_with_the_plugin_that_offered_it()
    {
        SettingRows.Attributed("Played by WASAPI (shared mode)", new PluginInfo("win.io", "Windows sound and MIDI"))
            .ShouldBe("Played by WASAPI (shared mode), from the Windows sound and MIDI plugin (win.io).");

        SettingRows.Attributed("Played by WASAPI (shared mode)", null)
            .ShouldBe("Played by WASAPI (shared mode).");
    }

    /// <summary>
    /// A sound input backend's question is asked on the Sound tab, under the name of the
    /// plugin that offered it, and the answer is kept apart from the sound output's.
    /// </summary>
    [AvaloniaFact]
    public void The_sound_input_is_chosen_on_the_sound_tab_and_kept_under_its_own_backend()
    {
        var plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], audioInputs: [new TwoMicrophones()]);
        var window = Open(settingsPath, plugins);
        var dialog = OpenSettings(window, SoundTab);

        Named<TextBlock>(dialog, "inputNote").Text.ShouldBe("Heard by Two microphones, for a Line In.");

        var choice = All<ComboBox>(Named<SettingsForm>(dialog, "inputForm")).Single();
        choice.SelectedIndex.ShouldBe(0);

        choice.SelectedIndex = 1;
        CloseSettings(window, dialog);

        var kept = OutputSettings.Load(settingsPath);
        kept.SoundInOf("two").Text("device", "").ShouldBe("rear");
        kept.SoundOf("two").All.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Without_a_sound_input_the_sound_tab_asks_nothing_about_one()
    {
        var window = Open();
        var dialog = OpenSettings(window, SoundTab);

        All<SettingsForm>(dialog).Select(form => form.Name).ShouldNotContain("inputForm");
    }

    /// <summary>
    /// Where two backends can play, the tab asks which, shows the picked one's own
    /// questions under it, and keeps the pick and its answers for the next launch.
    /// </summary>
    [AvaloniaFact]
    public void Where_two_backends_can_play_the_sound_tab_asks_which()
    {
        var plugins = new PluginCatalog([], [new Backend("wasapi", "WASAPI", 100), new Backend("asio", "ASIO", 50)], NodeCatalog.BuiltIn, [.. Presets.All], []);
        var window = Open(settingsPath, plugins);
        var dialog = OpenSettings(window, SoundTab);
        var through = Named<ComboBox>(dialog, "soundOutput");

        through.ItemsSource.ShouldBe(new[] { "WASAPI", "ASIO" });
        through.SelectedIndex.ShouldBe(0);
        Named<TextBlock>(dialog, "soundNote").Text.ShouldBe("Played by WASAPI.");

        through.SelectedIndex = 1;
        Settle(window);

        Named<TextBlock>(dialog, "soundNote").Text.ShouldBe("Played by ASIO.");
        var device = All<ComboBox>(Named<SettingsForm>(dialog, "soundForm")).Single();
        device.SelectedIndex = 1;

        CloseSettings(window, dialog);

        var kept = OutputSettings.Load(settingsPath);
        kept.SoundOutput.ShouldBe("asio");
        kept.SoundOf("asio").Text("device", "").ShouldBe("asio-second");
        kept.SoundOf("wasapi").All.ShouldBeEmpty();

        Named<ComboBox>(OpenSettings(Open(settingsPath, plugins), SoundTab), "soundOutput").SelectedIndex.ShouldBe(1);
    }

    /// <summary>Saving without touching the pick names no backend, so one that starts to outrank it later still wins.</summary>
    [AvaloniaFact]
    public void Saving_without_picking_leaves_the_backend_to_the_ranking()
    {
        var plugins = new PluginCatalog([], [new Backend("wasapi", "WASAPI", 100), new Backend("asio", "ASIO", 50)], NodeCatalog.BuiltIn, [.. Presets.All], []);
        var window = Open(settingsPath, plugins);
        var dialog = OpenSettings(window, SoundTab);

        Latency(dialog).SelectedIndex = 0;
        CloseSettings(window, dialog);

        OutputSettings.Load(settingsPath).SoundOutput.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Where_one_backend_can_play_the_sound_tab_does_not_ask_which()
    {
        var plugins = new PluginCatalog([], [new Backend("wasapi", "WASAPI", 100), new Backend("asio", "ASIO", 50, Supported: false)], NodeCatalog.BuiltIn, [.. Presets.All], []);
        var dialog = OpenSettings(Open(settingsPath, plugins), SoundTab);

        All<ComboBox>(dialog).Select(c => c.Name).ShouldNotContain("soundOutput");
        Named<TextBlock>(dialog, "soundNote").Text.ShouldBe("Played by WASAPI.");
    }

    private sealed record Backend(string Id, string Name, int Priority, bool Supported = true) : IAudioOutput
    {
        public bool IsSupported => Supported;

        public IReadOnlyList<SettingField> Form(SettingValues values) =>
            [new SettingField.Pick("device", "Device", [new SettingOption($"{Id}-first", "First"), new SettingOption($"{Id}-second", "Second")], $"{Id}-first")];

        public IAudioDevice Create(AudioFormat format, SettingValues settings) => new SilentAudioDevice(format.SampleRate);
    }

    private sealed class TwoMicrophones : IAudioInput
    {
        public string Id => "two";

        public string Name => "Two microphones";

        public int Priority => 0;

        public bool IsSupported => true;

        public IReadOnlyList<SettingField> Form(SettingValues values) =>
            [new SettingField.Pick("device", "Input", [new SettingOption("front", "Front"), new SettingOption("rear", "Rear")], "front")];

        public IAudioCapture Create(AudioFormat format, SettingValues settings) => throw new NotSupportedException();
    }

    [AvaloniaFact]
    public void Recording_and_sound_start_on_the_defaults()
    {
        var window = Open();

        var recording = OpenSettings(window, RecordingTab);

        (FrameRate(recording).SelectedItem as string).ShouldBe("30 fps");
        Quality(recording).Value.ShouldBe(85);
        (CountIn(recording).SelectedItem as string).ShouldBe("3 s");
        RewindFirst(recording).IsChecked.ShouldBe(true);

        ShowSettingsTab(recording, SoundTab);
        Settle(window);

        (Latency(recording).SelectedItem as string).ShouldBe("30 ms");
    }

    [AvaloniaFact]
    public void Recording_and_sound_are_kept_for_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        FrameRate(dialog).SelectedIndex = 4;
        Quality(dialog).Value = 60;
        CountIn(dialog).SelectedIndex = 0;
        RewindFirst(dialog).IsChecked = false;

        ShowSettingsTab(dialog, SoundTab);
        Settle(window);

        Latency(dialog).SelectedIndex = 0;

        CloseSettings(window, dialog);

        var kept = OutputSettings.Load(settingsPath);

        kept.FrameRate.ShouldBe(60);
        kept.JpegQuality.ShouldBe(60);
        kept.LatencyMilliseconds.ShouldBe(OutputSettings.ShortestLatency);
        kept.CountInSeconds.ShouldBe(OutputSettings.NoCountIn);
        kept.RewindBeforeTake.ShouldBeFalse();

        var again = OpenSettings(Open(settingsPath), RecordingTab);

        (FrameRate(again).SelectedItem as string).ShouldBe("60 fps");
        Quality(again).Value.ShouldBe(60);
        (CountIn(again).SelectedItem as string).ShouldBe("None");
        RewindFirst(again).IsChecked.ShouldBe(false);
    }

    [AvaloniaFact]
    public void Recording_and_sound_changes_are_dropped_without_save()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window, RecordingTab);

        FrameRate(dialog).SelectedIndex = 0;
        Quality(dialog).Value = 20;
        CountIn(dialog).SelectedIndex = 0;
        RewindFirst(dialog).IsChecked = false;

        CloseSettings(window, dialog, "cancel");

        File.Exists(settingsPath).ShouldBeFalse();

        var again = OpenSettings(window, RecordingTab);

        (FrameRate(again).SelectedItem as string).ShouldBe("30 fps");
        Quality(again).Value.ShouldBe(85);
        (CountIn(again).SelectedItem as string).ShouldBe("3 s");
        RewindFirst(again).IsChecked.ShouldBe(true);
    }
}
