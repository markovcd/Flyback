using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Engine.Render;
using Flyback.Editor.Controls;
using Flyback.Editor.Inspect;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Audio;
using Flyback.Ui.Controls;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Settings;

/// <summary>
/// The Sound section of the settings window: which backend plays where more than one can,
/// whatever it declares, then how far behind the patch the speakers may run.
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

    private readonly StackPanel rows = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    /// <summary>Which backend plays, offered only where more than one can.</summary>
    private readonly ComboBox through = new Picker
    {
        Name = "soundOutput",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Every backend that can play here, in the order <see cref="through"/> lists them.</summary>
    private readonly IReadOnlyList<IAudioOutput> playable;

    /// <summary>The backend the saved settings play through, which a Save leaves named as it was unless another is picked.</summary>
    private IAudioOutput? saved;

    /// <summary>The backend whose form is showing.</summary>
    private IAudioOutput? shown;

    private readonly ComboBox latency = new Picker
    {
        Name = "latency",
        ItemsSource = Latencies.Select(ms => $"{ms} ms").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly CheckBox stepDown = new()
    {
        Name = "stepDown",
        Content = "Lower oversampling when the sound drops out",
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

    /// <summary>
    /// The sound input backend's own settings — which microphone a Line In hears — drawn
    /// from what it declares, like <see cref="soundForm"/>. Empty where none is installed.
    /// </summary>
    private readonly SettingsForm inputForm = new() { Name = "inputForm", Beside = true };

    /// <summary>Which backend listens, and which plugin it came from, above the rows it asks for.</summary>
    private readonly TextBlock inputNote = new()
    {
        Name = "inputNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

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

        playable = plugins.PlayableAudioOutputs;
        through.ItemsSource = playable.Select(output => output.Name).ToList();
        through.SelectionChanged += (_, _) =>
        {
            if (through.SelectedIndex >= 0 && playable[through.SelectedIndex] != shown) Present(playable[through.SelectedIndex], settings.Current);
        };

        soundNote.Text = "No sound plugin is installed, so nothing plays. See About for where plugins are looked for.";

        ToolTip.SetTip(through,
            "Which of the installed ways of playing sound Flyback plays through. "
            + "One that cannot play at launch is passed over for the next.");

        // The note first, so the rows under it are read as the backend's answers
        // rather than Flyback's; then the form, because which device plays is the
        // question people come to this tab with. Nothing here knows what the
        // backend will ask (ADR-0085).
        rows.Children.Add(soundNote);

        if (playable.Count > 1) rows.Children.Add(InspectorRows.Field("Play through", through));

        if (playable.Count > 0) rows.Children.Add(soundForm);

        rows.Children.Add(InspectorRows.Field("Latency", latency));

        // A page has no microphone to offer, and a machine with no input plugin says nothing.
        if (!host.InPage && plugins.PreferredAudioInput is { } input)
        {
            inputNote.Text = SettingRows.Attributed($"Heard by {input.Name}, for a Line In", plugins.Provider(input));
            rows.Children.Add(inputNote);
            rows.Children.Add(inputForm);
        }

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

        if (InputChanged(before, settings.Current)) playback.ReopenInput();
    }

    private void Show(OutputSettings current)
    {
        latency.SelectedIndex = SettingRows.Nearest(Latencies.Select(ms => (double)ms).ToArray(), current.LatencyMilliseconds);
        oversample.SelectedIndex = Math.Max(0, AudioRenderer.Oversamples.ToList().IndexOf(current.Oversample));
        stepDown.IsChecked = current.StepDownOnDropouts;

        saved = plugins.AudioOutput(current.SoundOutput);

        if (saved is not null)
        {
            Present(saved, current);
            through.SelectedIndex = playable.ToList().IndexOf(saved);
        }

        if (plugins.PreferredAudioInput is { } input)
            inputForm.Show(input.Form, current.SoundInOf(input.Id));
    }

    /// <summary>Shows what <paramref name="output"/> asks, answered as <paramref name="current"/> holds.</summary>
    private void Present(IAudioOutput output, OutputSettings current)
    {
        shown = output;
        soundNote.Text = SettingRows.Attributed($"Played by {output.Name}", plugins.Provider(output));
        soundForm.Show(output.Form, current.SoundOf(output.Id));
    }

    /// <summary>Writes what the controls hold into <paramref name="into"/>.</summary>
    /// <remarks>Only the backend showing is written, so one not installed this launch keeps what it was set to.</remarks>
    private void Read(OutputSettings into)
    {
        into.LatencyMilliseconds = Latencies[Math.Max(latency.SelectedIndex, 0)];
        into.Oversample = AudioRenderer.Oversamples[Math.Max(oversample.SelectedIndex, 0)];
        into.StepDownOnDropouts = stepDown.IsChecked == true;

        if (shown is not null) into.RememberSound(shown.Id, soundForm.Values);
        if (shown != saved) into.SoundOutput = shown!.Id;
        if (plugins.PreferredAudioInput is { } input) into.RememberSoundIn(input.Id, inputForm.Values);
    }

    /// <summary>
    /// Whether the sound backend's answers mean something else in
    /// <paramref name="after"/> than in <paramref name="before"/>, read the way the
    /// backend reads them — so a device picked and then picked back is not a
    /// change, though the bag now holds a key it did not.
    /// </summary>
    private bool SoundChanged(OutputSettings before, OutputSettings after)
    {
        if (plugins.AudioOutput(after.SoundOutput) is not { } output) return false;

        if (output != plugins.AudioOutput(before.SoundOutput)) return true;

        var now = after.SoundOf(output.Id);

        return !output.Form(now).All(field =>
            field.Sane(before.SoundOf(output.Id).All.GetValueOrDefault(field.Key)) == field.Sane(now.All.GetValueOrDefault(field.Key)));
    }

    /// <summary>The input backend's counterpart of <see cref="SoundChanged"/>.</summary>
    private bool InputChanged(OutputSettings before, OutputSettings after)
    {
        if (plugins.PreferredAudioInput is not { } input) return false;

        var now = after.SoundInOf(input.Id);

        return !input.Form(now).All(field =>
            field.Sane(before.SoundInOf(input.Id).All.GetValueOrDefault(field.Key)) == field.Sane(now.All.GetValueOrDefault(field.Key)));
    }
}
