using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Editor.Controls;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;
using Flyback.Host;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Capture;

/// <summary>
/// The Send section of the settings window: which picture output the preview is sent
/// to, such as NDI, and whatever that output declares (ADR-0240). Listed only where a
/// plugin offers one.
/// </summary>
internal sealed class SendingSection : ISettingsSection, IDisposable
{
    /// <summary>The rate an uncapped preview is sent at: what VJ software mixes at.</summary>
    public const double UncappedRate = 60;

    private readonly PluginCatalog plugins;
    private readonly OutputSettingRepository settings;
    private readonly PreviewHost preview;
    private readonly ReportLine report;

    public string Name => "Send";

    public string Group => SettingsGroups.Output;

    public Control View => rows;

    private readonly StackPanel rows = new() { Spacing = 8, Width = SettingsSession.SectionWidth };

    /// <summary>Off, then every installed output.</summary>
    private readonly ComboBox to = new Picker
    {
        Name = "sendTo",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Every installed output, in the order <see cref="to"/> lists them after Off.</summary>
    private readonly IReadOnlyList<IVideoOutput> outputs;

    /// <summary>The output's own settings, such as the name it is seen by on the network.</summary>
    private readonly SettingsForm form = new() { Name = "sendForm", Beside = true };

    /// <summary>Which output is picked, and which plugin it came from.</summary>
    private readonly TextBlock note = new()
    {
        Name = "sendNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>The output whose form is showing, or null for Off.</summary>
    private IVideoOutput? shown;

    /// <summary>What is being sent now, and what it was opened with, or null when nothing is.</summary>
    private Sending? running;

    public SendingSection(PluginCatalog plugins, OutputSettingRepository settings, PreviewHost preview, ReportLine report)
    {
        this.plugins = plugins;
        this.settings = settings;
        this.preview = preview;
        this.report = report;

        outputs = [.. plugins.VideoOutputs.OrderBy(o => o.Id, StringComparer.Ordinal)];
        to.ItemsSource = (List<string>)["Off", .. outputs.Select(Label)];
        to.SelectionChanged += (_, _) => Present(to.SelectedIndex > 0 ? outputs[to.SelectedIndex - 1] : null, settings.Current);

        ToolTip.SetTip(to,
            "Sends the preview, as it is drawn, to other programs such as VJ software, where it shows up "
            + "as a live source. It goes at the preview's size and rate, up to 60 frames a second, and stops "
            + "when the picture is drawn on the CPU.");

        rows.Children.Add(SettingRows.Heading("Send the picture"));
        rows.Children.Add(InspectorRows.Field("Send to", to));
        rows.Children.Add(note);
        rows.Children.Add(form);

        preview.CaptureLost += message =>
        {
            if (running is null) return;

            Stop();
            report.Say(message);
        };

        // Back on the GPU, a picture that was being sent is sent again.
        preview.BackendChanged += _ =>
        {
            if (running is null && preview.Backend == PreviewBackend.Gpu) Apply(settings.Current, quiet: true);
        };
    }

    /// <summary>The output being sent to, or null; tests read it back.</summary>
    internal IVideoOutput? SendingTo => running?.Output;

    /// <summary>Frames sent since sending last started; tests read it back.</summary>
    internal long Sent => running?.Sender.Sent ?? 0;

    public void Start()
    {
        Show();
        Apply(settings.Current, quiet: false);
    }

    public void Show() => Show(settings.Current);

    public void Save()
    {
        settings.Change(Read);
        Apply(settings.Current, quiet: false);
    }

    private void Show(OutputSettings current)
    {
        var saved = outputs.FirstOrDefault(o => o.Id == current.PictureOutput);

        to.SelectedIndex = saved is null ? 0 : outputs.ToList().IndexOf(saved) + 1;
        Present(saved, current);
    }

    /// <summary>Shows what <paramref name="output"/> asks, answered as <paramref name="current"/> holds.</summary>
    private void Present(IVideoOutput? output, OutputSettings current)
    {
        shown = output;

        if (output is null)
        {
            note.Text = "Nothing is sent.";
            form.Show(null, SettingValues.None);
            return;
        }

        note.Text = SettingRows.Attributed($"Sent by {output.Name}", plugins.Provider(output));
        form.Show(output.Form, current.PictureOutputOf(output.Id));
    }

    /// <summary>Writes what the controls hold into <paramref name="into"/>; an output not showing keeps what it was set to.</summary>
    private void Read(OutputSettings into)
    {
        into.PictureOutput = shown?.Id ?? string.Empty;

        if (shown is not null) into.RememberPictureOutput(shown.Id, form.Values);
    }

    /// <summary>Sends what <paramref name="now"/> asks for, leaving a sender already sending that alone.</summary>
    /// <param name="quiet">Whether to keep a refusal off the report line, for a retry nobody asked for.</param>
    private void Apply(OutputSettings now, bool quiet)
    {
        var output = outputs.FirstOrDefault(o => o.Id == now.PictureOutput);
        var values = output is null ? SettingValues.None : now.PictureOutputOf(output.Id);
        var rate = now.PreviewFrameRate > 0 ? Math.Min(now.PreviewFrameRate, UncappedRate) : UncappedRate;

        if (running is { } current && current.Output == output && current.Rate == rate && Same(output, current.Values, values)) return;

        var was = running?.Output;
        Stop();

        if (output is null)
        {
            if (was is not null) report.Say($"Stopped sending the picture to {was.Name}.");
            return;
        }

        PictureSender? sender = null;
        sender = new PictureSender(output, values, rate, why => Dispatcher.UIThread.Post(() =>
        {
            // A sender already replaced has nothing left to stop.
            if (running?.Sender != sender) return;

            Stop();
            report.Say(why);
        }));

        if (preview.BeginCapture(sender) is { } refused)
        {
            sender.Dispose();
            if (!quiet) report.Say($"Could not send the picture to {output.Name}: {refused}");
            return;
        }

        running = new Sending(output, values, rate, sender);
        report.Say($"Sending the picture to {output.Name}.");
    }

    private void Stop()
    {
        if (running is not { } current) return;

        running = null;
        preview.EndCapture(current.Sender);
        current.Sender.Dispose();
    }

    /// <summary>Whether two sets of answers mean the same to <paramref name="output"/>, read the way it reads them.</summary>
    private static bool Same(IVideoOutput? output, SettingValues before, SettingValues after) =>
        output is null || output.Form(after).All(field =>
            field.Sane(before.All.GetValueOrDefault(field.Key)) == field.Sane(after.All.GetValueOrDefault(field.Key)));

    /// <summary>An output as the picker lists it, saying so when it cannot run here; picking it anyway lets it say why.</summary>
    private string Label(IVideoOutput output) =>
        plugins.UsableVideoOutputs.Contains(output) ? output.Name : $"{output.Name} (not ready here)";

    public void Dispose() => Stop();

    /// <summary>One output being sent to, and what it was opened with.</summary>
    private sealed record Sending(IVideoOutput Output, SettingValues Values, double Rate, PictureSender Sender);
}
