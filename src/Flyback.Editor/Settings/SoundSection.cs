using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Render;
using Flyback.Editor.Controls;
using Flyback.Editor.Inspect;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Audio;
using Flyback.Ui.Controls;
using Flyback.Ui;

namespace Flyback.Editor.Settings;

/// <summary>
/// The Sound section of the settings window: whatever the sound backend declares,
/// then how far behind the patch the speakers may run.
/// </summary>
internal sealed class SoundSection : ISettingsSection
{
    /// <summary>The latencies the speakers can be asked for, in milliseconds.</summary>
    private static readonly int[] Latencies = [5, 10, 20, 30, 50, 100, 200];

    private readonly PluginCatalog plugins;
    private readonly OutputSettingRepository settings;
    private readonly IAudioEngine audio;
    private readonly Playback playback;

    /// <summary>What lowers the oversampling when the sound keeps falling behind.</summary>
    private readonly LiveOversample live;

    public string Name => "Sound";

    public Control View => rows;

    private readonly StackPanel rows = new() { Spacing = 8, Width = 280 };

    private readonly ComboBox latency = new Picker
    {
        Name = "latency",
        ItemsSource = Latencies.Select(ms => $"{ms} ms").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly CheckBox stepDown = new()
    {
        Name = "stepDown",
        Content = "Lower it when the sound drops out",
    };

    /// <summary>How many times the output rate the sound is evaluated at, one row a factor of <see cref="AudioRenderer.Oversamples"/>.</summary>
    private readonly ComboBox oversample = new Picker
    {
        Name = "oversample",
        ItemsSource = AudioRenderer.Oversamples.Select(OversamplingText.Choice).ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// The sound backend's own settings — which device plays, for one — drawn from
    /// what it declares (ADR-0085). Empty where no backend is installed or it has
    /// nothing to ask.
    /// </summary>
    private readonly SettingsForm soundForm = new() { Name = "soundForm", Beside = true };

    /// <summary>Which backend plays, and which plugin it came from, above the rows it asks for.</summary>
    private readonly TextBlock soundNote = new()
    {
        Name = "soundNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    public SoundSection(PluginCatalog plugins, EditorHost host, OutputSettingRepository settings, IAudioEngine audio, Playback playback, ReportLine report)
    {
        this.plugins = plugins;
        this.settings = settings;
        this.audio = audio;
        this.playback = playback;

        live = new(audio, () => settings.Current.StepDownOnDropouts, message => report.Say(message));

        ToolTip.SetTip(latency,
            "How far behind the patch the speakers may run. Lower answers a key sooner; "
            + "raise it if the sound crackles. Flyback asks this of every backend, "
            + "whichever plugin is playing.");

        soundNote.Text = plugins.PreferredAudioOutput is { } output
            ? SettingRows.Attributed($"Played by {output.Name}", plugins.Provider(output))
            : "No sound plugin is installed, so nothing plays. See About for where plugins are looked for.";

        // The note first, so the rows under it are read as the backend's answers
        // rather than Flyback's; then the form, because which device plays is the
        // question people come to this tab with. Nothing here knows what the
        // backend will ask (ADR-0085).
        rows.Children.Add(soundNote);

        if (plugins.PreferredAudioOutput is not null) rows.Children.Add(soundForm);

        rows.Children.Add(InspectorRows.Field("Latency", latency));

        // A page lowers its oversampling itself as the sound needs, so it has none to pick.
        if (host.InPage) return;

        ToolTip.SetTip(oversample,
            "How many times the output rate the sound is worked out at before it is filtered down. "
            + "Higher is cleaner on bright raw saws and costs more; 2× is clean enough for nearly "
            + "every patch. A take, flyback-cli render and the viewer use it too.");

        rows.Children.Add(InspectorRows.Field("Oversampling", oversample));

        ToolTip.SetTip(stepDown,
            "When the sound keeps falling behind while it plays, work it out a step lower, 4× to 2× to 1×, "
            + "rather than let it stutter. Not while a take is recorded, which is written whole whatever the "
            + "speakers do; a render keeps the setting above too.");

        rows.Children.Add(stepDown);
    }

    public void Start()
    {
        Show();
        audio.Oversample = settings.Current.Oversample;
        live.Start();
    }

    public void Show() => Show(settings.Current);

    public void Save()
    {
        var before = settings.Change(Read);

        audio.Oversample = settings.Current.Oversample;

        if (settings.Current.LatencyMilliseconds != before.LatencyMilliseconds || SoundChanged(before, settings.Current))
            playback.ReopenAudio(settings.Current);
    }

    private void Show(OutputSettings current)
    {
        latency.SelectedIndex = SettingRows.Nearest(Latencies.Select(ms => (double)ms).ToArray(), current.LatencyMilliseconds);
        oversample.SelectedIndex = Math.Max(0, AudioRenderer.Oversamples.ToList().IndexOf(current.Oversample));
        stepDown.IsChecked = current.StepDownOnDropouts;

        if (plugins.PreferredAudioOutput is { } output)
            soundForm.Show(output.Form, current.SoundOf(output.Id));
    }

    /// <summary>Writes what the controls hold into <paramref name="into"/>.</summary>
    /// <remarks>Only the backend showing is written, so one not installed this launch keeps what it was set to.</remarks>
    private void Read(OutputSettings into)
    {
        into.LatencyMilliseconds = Latencies[Math.Max(latency.SelectedIndex, 0)];
        into.Oversample = AudioRenderer.Oversamples[Math.Max(oversample.SelectedIndex, 0)];
        into.StepDownOnDropouts = stepDown.IsChecked == true;

        if (plugins.PreferredAudioOutput is { } output) into.RememberSound(output.Id, soundForm.Values);
    }

    /// <summary>
    /// Whether the sound backend's answers mean something else in
    /// <paramref name="after"/> than in <paramref name="before"/>, read the way the
    /// backend reads them — so a device picked and then picked back is not a
    /// change, though the bag now holds a key it did not.
    /// </summary>
    private bool SoundChanged(OutputSettings before, OutputSettings after)
    {
        if (plugins.PreferredAudioOutput is not { } output) return false;

        var now = after.SoundOf(output.Id);

        return !output.Form(now).All(field =>
            field.Sane(before.SoundOf(output.Id).All.GetValueOrDefault(field.Key)) == field.Sane(now.All.GetValueOrDefault(field.Key)));
    }
}
