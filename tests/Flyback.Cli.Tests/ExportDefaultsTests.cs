using Flyback.Core.Render;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>What <c>render</c> takes from the editor's settings when a flag is left out.</summary>
public class ExportDefaultsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-export-defaults-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(folder, "output.json");

    public ExportDefaultsTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        Directory.Delete(folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void No_file_is_the_editors_defaults()
    {
        var defaults = ExportDefaults.Load(File);

        defaults.ShouldBe(new ExportDefaults(960, 540, 30d, 85));
    }

    [Fact]
    public void A_render_follows_the_preview_size_and_the_recording_settings()
    {
        System.IO.File.WriteAllText(File, """
            {
              "width": 1280,
              "height": 720,
              "gpu": true,
              "frameRate": 60,
              "jpegQuality": 70,
              "videoFormat": "hevc",
              "soundFormat": "flac",
              "ffmpegPath": "C:\\tools\\ffmpeg.exe",
              "takeover": 1
            }
            """);

        var defaults = ExportDefaults.Load(File);

        defaults.ShouldBe(new ExportDefaults(
            1280, 720, 60d, 70, ClipFormats.ById("hevc"), ClipFormats.ById("flac"), @"C:\tools\ffmpeg.exe"));
    }

    [Fact]
    public void A_value_edited_out_of_range_is_brought_back_into_it()
    {
        System.IO.File.WriteAllText(File, """{ "width": -4, "height": 720, "frameRate": 0, "jpegQuality": 400, "videoFormat": "wav" }""");

        var defaults = ExportDefaults.Load(File);

        defaults.ShouldBe(new ExportDefaults(960, 540, 1d, 100));
    }

    [Fact]
    public void An_unreadable_file_is_the_editors_defaults()
    {
        System.IO.File.WriteAllText(File, "{ not json");

        ExportDefaults.Load(File).ShouldBe(new ExportDefaults());
    }

    [Theory]
    [InlineData("take.mp4", "hevc")]
    [InlineData("take.flac", "flac")]
    [InlineData("take.webm", null)]
    [InlineData("take.png", null)]
    public void The_saved_format_wins_where_the_extension_is_its_own(string name, string? format)
    {
        var defaults = new ExportDefaults(Video: ClipFormats.ById("hevc"), Sound: ClipFormats.ById("flac"));

        defaults.FormatFor(name).ShouldBe(format);
    }

    [Theory]
    [InlineData(new[] { "render", "a.fbk", "--settings", "x.json" }, "x.json")]
    [InlineData(new[] { "render", "a.fbk", "--settings=y.json" }, "y.json")]
    [InlineData(new[] { "render", "a.fbk" }, null)]
    public void Another_settings_file_is_found_before_the_command_is_built(string[] args, string? path) =>
        ExportDefaults.PathIn(args).ShouldBe(path);
}
