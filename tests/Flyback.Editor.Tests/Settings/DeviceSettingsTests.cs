using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Shell;
using Flyback.Editor.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Settings;

/// <summary>On a phone or tablet, Settings leaves out the rows only a desktop acts on (ADR-0184).</summary>
public sealed class DeviceSettingsTests : EditorTest
{
    private MainWindow Open(bool onDevice)
    {
        var window = NewMainWindow(new EditorSetup { Host = new() { OnDevice = onDevice } });

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static string[] Named(ModalOverlay dialog) =>
        [.. All<Control>(dialog).Select(c => c.Name).OfType<string>()];

    [AvaloniaTheory]
    [InlineData("Files", "fileOpener")]
    [InlineData("Canvas", "dragToPan")]
    [InlineData("Canvas", "measureWindow")]
    [InlineData("Canvas", "measurePicture")]
    [InlineData("Full screen", "fullScreenOn")]
    [InlineData("Recording", "videoFormat")]
    [InlineData("Recording", "soundFormat")]
    [InlineData("Recording", "ffmpeg")]
    public void A_device_leaves_out_what_only_a_desktop_acts_on(string tab, string row)
    {
        Named(OpenSettings(Open(onDevice: false), tab)).ShouldContain(row);
        Named(OpenSettings(Open(onDevice: true), tab)).ShouldNotContain(row);
    }

    [AvaloniaFact]
    public void A_device_takes_the_formats_written_here()
    {
        var window = Open(onDevice: true);

        CloseSettings(window, OpenSettings(window, "Recording"));

        var current = Service<OutputSettingRepository>(window).Current;
        current.VideoFormat.ShouldBe(Engine.Render.ClipFormats.MotionJpegAvi.Id);
        current.SoundFormat.ShouldBe(Engine.Render.ClipFormats.Wav.Id);
    }
}
