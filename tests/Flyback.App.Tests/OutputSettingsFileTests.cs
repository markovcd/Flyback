using Flyback.Core.Render;
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
    public void The_full_screen_monitor_comes_back()
    {
        new OutputSettings
        {
            FullScreen = FullScreenOn.ChosenMonitor,
            FullScreenMonitor = new MonitorSpot { Name = "right", X = 1920, Width = 2560, Height = 1440 },
        }.Save(File);

        var settings = OutputSettings.Load(File);

        settings.FullScreen.ShouldBe(FullScreenOn.ChosenMonitor);
        settings.FullScreenMonitor.ShouldNotBeNull().Name.ShouldBe("right");
        settings.FullScreenMonitor.X.ShouldBe(1920);
    }

    [Fact]
    public void A_full_screen_choice_this_build_does_not_have_is_the_same_monitor()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "fullScreen": 42 }""");

        OutputSettings.Load(File).FullScreen.ShouldBe(FullScreenOn.SameMonitor);
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

    /// <summary>How a take begins, which is two settings of its own (ADR-0091).</summary>
    [Fact]
    public void How_a_take_begins_comes_back()
    {
        new OutputSettings { CountInSeconds = 10, RewindBeforeTake = false }.Save(File);

        var settings = OutputSettings.Load(File);

        settings.CountInSeconds.ShouldBe(10);
        settings.RewindBeforeTake.ShouldBeFalse();
    }

    /// <summary>A count of nought is none, which is a choice and not a value to be filled in.</summary>
    [Fact]
    public void No_count_in_comes_back_as_none()
    {
        new OutputSettings { CountInSeconds = OutputSettings.NoCountIn }.Save(File);

        OutputSettings.Load(File).CountInSeconds.ShouldBe(OutputSettings.NoCountIn);
    }

    /// <summary>A machine with no file counts three in, and starts at zero.</summary>
    [Fact]
    public void No_file_counts_a_take_in_and_rewinds_it()
    {
        var settings = OutputSettings.Load(File);

        settings.CountInSeconds.ShouldBe(OutputSettings.DefaultCountIn);
        settings.RewindBeforeTake.ShouldBeTrue();
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
        System.IO.File.WriteAllText(File, """{ "frameRate": 0, "jpegQuality": 400, "latencyMilliseconds": 60000, "countInSeconds": 600 }""");

        var settings = OutputSettings.Load(File);

        settings.FrameRate.ShouldBe(OutputSettings.SlowestFrameRate);
        settings.JpegQuality.ShouldBe(OutputSettings.HighestQuality);
        settings.LatencyMilliseconds.ShouldBe(OutputSettings.LongestLatency);
        settings.CountInSeconds.ShouldBe(OutputSettings.LongestCountIn);
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

    /// <summary>Which encoder a take goes through, and which ffmpeg (ADR-0089).</summary>
    [Fact]
    public void The_formats_and_the_ffmpeg_come_back()
    {
        new OutputSettings
        {
            VideoFormat = ClipFormats.Vp9WebM.Id,
            SoundFormat = ClipFormats.Mp3.Id,
            FfmpegPath = @"C:	oolsfmpeg.exe",
        }.Save(File);

        var settings = OutputSettings.Load(File);

        settings.VideoFormat.ShouldBe(ClipFormats.Vp9WebM.Id);
        settings.SoundFormat.ShouldBe(ClipFormats.Mp3.Id);
        settings.FfmpegPath.ShouldBe(@"C:	oolsfmpeg.exe");
    }

    /// <summary>
    /// A format id this build does not define, and one saved into the wrong list.
    /// Both read as the format written here, which is the one that always works.
    /// </summary>
    [Fact]
    public void A_format_nothing_defines_reads_as_the_one_written_here()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "videoFormat": "av1", "soundFormat": "mp4" }""");

        var settings = OutputSettings.Load(File);

        settings.VideoFormat.ShouldBe(ClipFormats.MotionJpegAvi.Id);
        settings.SoundFormat.ShouldBe(ClipFormats.Wav.Id);
    }

    /// <summary>
    /// A saved format is never second-guessed by what this machine has. Somebody
    /// who chose H.265 on a machine with ffmpeg and opened the program on one
    /// without it still has H.265 chosen when they go back.
    /// </summary>
    [Fact]
    public void A_saved_format_survives_a_machine_that_cannot_write_it()
    {
        new OutputSettings { VideoFormat = ClipFormats.H265Mp4.Id }.Save(File);

        OutputSettings.Load(File).VideoFormat.ShouldBe(ClipFormats.H265Mp4.Id);
    }

    /// <summary>Which preset the window opens on next, by name (ADR-0093).</summary>
    [Fact]
    public void The_startup_preset_comes_back()
    {
        new OutputSettings { DefaultPreset = "Kaleidoscope" }.Save(File);

        OutputSettings.Load(File).DefaultPreset.ShouldBe("Kaleidoscope");
    }

    /// <summary>No file, and no choice made yet, both read as the one written here.</summary>
    [Fact]
    public void No_file_has_no_startup_preset_chosen()
    {
        OutputSettings.Load(File).DefaultPreset.ShouldBe(string.Empty);
    }

    /// <summary>A "defaultPreset": null typed by hand is nothing chosen, not a fault.</summary>
    [Fact]
    public void A_null_startup_preset_reads_as_none_chosen()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, """{ "defaultPreset": null }""");

        OutputSettings.Load(File).DefaultPreset.ShouldBe(string.Empty);
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
