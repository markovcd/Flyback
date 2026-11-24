using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Capture;
using Flyback.Editor.Settings;
using Flyback.Editor.Tests.Capture;
using Flyback.Engine.Graph;
using Flyback.Host;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Tests.Settings;

/// <summary>The Send tab: which picture output the preview goes to, and what that output asks.</summary>
public class OutputSettingsSendTests : OutputSettingsTest
{
    private const string SendTab = "Send";

    private static PluginCatalog With(params HeldOutput[] outputs) =>
        new([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], videoOutputs: outputs);

    /// <summary>
    /// Every installed output is offered after Off, one that cannot run here saying so,
    /// and the pick and its name are kept for the next launch.
    /// </summary>
    [AvaloniaFact]
    public void The_send_tab_picks_an_output_and_keeps_what_it_asked()
    {
        var plugins = With(new HeldOutput("ndi"), new HeldOutput("syphon", supported: false));
        var window = Open(SettingsPath, plugins);
        var dialog = OpenSettings(window, SendTab);
        var to = Named<ComboBox>(dialog, "sendTo");

        to.ItemsSource.ShouldBe(new[] { "Off", "Held (ndi)", "Held (syphon) (not ready here)" });
        to.SelectedIndex.ShouldBe(0);
        Named<TextBlock>(dialog, "sendNote").Text.ShouldBe("Nothing is sent.");

        to.SelectedIndex = 1;
        Settle(window);

        Named<TextBlock>(dialog, "sendNote").Text.ShouldBe("Sent by Held (ndi).");
        var name = All<TextBox>(Named<SettingsForm>(dialog, "sendForm")).Single();
        name.Text = "Stage left";

        CloseSettings(window, dialog);

        var kept = OutputSettings.Load(SettingsPath);
        kept.PictureOutput.ShouldBe("ndi");
        kept.PictureOutputOf("ndi").Text("name").ShouldBe("Stage left");

        Named<ComboBox>(OpenSettings(Open(SettingsPath, plugins), SendTab), "sendTo").SelectedIndex.ShouldBe(1);
    }

    /// <summary>
    /// Saving a pick sends at once; the picture leaving the GPU stops it with the reason,
    /// since the CPU draws too slowly to read frames from, and picking Off stops it too.
    /// </summary>
    [AvaloniaFact]
    public void Saving_a_pick_sends_and_the_picture_leaving_the_gpu_stops_it()
    {
        var output = new HeldOutput("ndi");
        var window = Open(SettingsPath, With(output));
        var dialog = OpenSettings(window, SendTab);
        var sending = Service<SendingSection>(window);
        var report = Service<ReportLine>(window);

        Named<ComboBox>(dialog, "sendTo").SelectedIndex = 1;
        CloseSettings(window, dialog);

        sending.SendingTo.ShouldBe(output);
        report.History.ShouldContain("Sending the picture to Held (ndi).");

        Service<PreviewHost>(window).Use(PreviewBackend.Cpu);

        sending.SendingTo.ShouldBeNull();
        report.History.ShouldContain("The picture left the GPU renderer, so it is no longer recorded or sent.");

        Service<PreviewHost>(window).Use(PreviewBackend.Gpu);
        sending.SendingTo.ShouldBe(output);

        dialog = OpenSettings(window, SendTab);
        Named<ComboBox>(dialog, "sendTo").SelectedIndex = 0;
        CloseSettings(window, dialog);

        sending.SendingTo.ShouldBeNull();
        report.History.ShouldContain("Stopped sending the picture to Held (ndi).");
    }

    [AvaloniaFact]
    public void Without_a_picture_output_there_is_no_send_tab() =>
        All<TabItem>(OpenSettings(Open())).Select(tab => tab.Header).ShouldNotContain(SendTab);
}
