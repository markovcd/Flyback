using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>
/// The file the settings window's Graphics, Recording and Sound sections are kept in.
/// </summary>
/// <remarks>
/// Nothing here opens a window. What is worth pinning is that the file cannot
/// stop the program starting, and that what went in comes back out.
/// </remarks>
public class OutputSettingsFileTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-output-settings-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(folder, "output.json");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void No_file_is_the_defaults()
    {
        var settings = OutputSettings.Load(File);

        settings.Width.ShouldBe(960);
        settings.Height.ShouldBe(540);
        settings.Gpu.ShouldBeTrue();
    }

    [Fact]
    public void What_is_saved_comes_back()
    {
        new OutputSettings { Width = 320, Height = 180, Gpu = false }.Save(File);

        var settings = OutputSettings.Load(File);

        settings.Width.ShouldBe(320);
        settings.Height.ShouldBe(180);
        settings.Gpu.ShouldBeFalse();
    }

    [Fact]
    public void How_a_controller_takes_over_a_knob_comes_back()
    {
        new OutputSettings { Takeover = App.Midi.Takeover.PickUp }.Save(File);

        OutputSettings.Load(File).Takeover.ShouldBe(App.Midi.Takeover.PickUp);
    }

    [Fact]
    public void Recording_and_sound_come_back()
    {
        new OutputSettings { FrameRate = 60, JpegQuality = 40, LatencyMilliseconds = 100 }.Save(File);

        var settings = OutputSettings.Load(File);

        settings.FrameRate.ShouldBe(60);
        settings.JpegQuality.ShouldBe(40);
        settings.LatencyMilliseconds.ShouldBe(100);
    }

    /// <summary>
    /// A file edited by hand is brought into range rather than trusted: a frame
    /// rate of nought would divide by it, and a latency of a minute would stall
    /// the sound.
    /// </summary>
    [Fact]
    public void Values_out_of_range_are_brought_into_it()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "frameRate": 0, "jpegQuality": 400, "latencyMilliseconds": 60000 }""");

        var settings = OutputSettings.Load(File);

        settings.FrameRate.ShouldBe(OutputSettings.SlowestFrameRate);
        settings.JpegQuality.ShouldBe(OutputSettings.HighestQuality);
        settings.LatencyMilliseconds.ShouldBe(OutputSettings.LongestLatency);
    }

    /// <summary>
    /// What a sound backend declared comes back under its own id, and nothing here
    /// has to know what any of it means (ADR-0085).
    /// </summary>
    [Fact]
    public void A_backends_answers_come_back_under_its_id()
    {
        var saved = new OutputSettings();

        saved.RememberSound("wasapi", SettingValues.None.With("device", "{0.0.0.00000000}.{speakers}"));
        saved.RememberSound("alsa", SettingValues.None.With("device", "hw:1"));
        saved.Save(File);

        var settings = OutputSettings.Load(File);

        settings.SoundOf("wasapi").Text("device").ShouldBe("{0.0.0.00000000}.{speakers}");
        settings.SoundOf("alsa").Text("device").ShouldBe("hw:1");
        settings.SoundOf("coreaudio").ShouldBe(SettingValues.None);
    }

    [Fact]
    public void A_file_with_no_sound_answers_has_none()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "sound": null }""");

        OutputSettings.Load(File).SoundOf("wasapi").ShouldBe(SettingValues.None);
    }

    /// <summary>Losing a preference is not worth failing to start over.</summary>
    [Fact]
    public void A_file_that_is_not_json_is_the_defaults()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "{ not settings");

        OutputSettings.Load(File).Gpu.ShouldBeTrue();
    }
}
