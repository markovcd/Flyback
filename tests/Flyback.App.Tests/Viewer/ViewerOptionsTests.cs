using System.CommandLine;
using Avalonia;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Flyback.Viewer;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Viewer;

/// <summary>
/// What a command line comes to once the settings file is behind it: every flag, the
/// defaults it overrides, and the combinations that are refused.
/// </summary>
public class ViewerOptionsTests
{
    private static readonly OutputSettings Machine = new()
    {
        Width = 1280,
        Height = 720,
        Gpu = false,
        PreviewFrameRate = 30,
        LatencyMilliseconds = 60,
        Transport = TransportEdge.Bottom,
    };

    private sealed record Ran(int Code, ViewerOptions? Options, string Error, int ParseErrors);

    private static Ran Run(OutputSettings settings, params string[] args)
    {
        ViewerOptions? seen = null;
        var error = new StringWriter();

        var root = ViewerArguments.Build(settings, options =>
        {
            seen = options;

            return Exit.Ok;
        }, error);

        var parsed = root.Parse(args);
        var code = parsed.Invoke(new InvocationConfiguration { Output = new StringWriter(), Error = error });

        return new Ran(code, seen, error.ToString(), parsed.Errors.Count);
    }

    private static ViewerOptions Settled(OutputSettings settings, params string[] args)
    {
        var ran = Run(settings, args);

        ran.Error.ShouldBeEmpty();

        return ran.Options ?? throw new InvalidOperationException("the command never ran");
    }

    /// <summary>NaN compares false with everything, so a range check alone lets it through.</summary>
    [Theory]
    [InlineData("--for", "NaN")]
    [InlineData("--loop", "NaN")]
    [InlineData("--fps", "NaN")]
    [InlineData("--from", "NaN")]
    [InlineData("--volume", "NaN")]
    [InlineData("--for", "Infinity")]
    public void A_number_that_is_not_one_is_refused(string flag, string typed)
    {
        var ran = Run(Machine, flag, typed);

        ran.ParseErrors.ShouldBeGreaterThan(0);
        ran.Options.ShouldBeNull();
    }

    /// <summary>What does not read as a number is the parser's to refuse, not a validator's to throw on.</summary>
    [Theory]
    [InlineData("--for")]
    [InlineData("--fps")]
    [InlineData("--volume")]
    [InlineData("--latency")]
    public void A_word_where_a_number_goes_is_refused(string flag)
    {
        var ran = Run(Machine, flag, "abc");

        ran.ParseErrors.ShouldBeGreaterThan(0);
        ran.Options.ShouldBeNull();
    }

    [Fact]
    public void With_no_flags_the_settings_file_is_the_run()
    {
        var options = Settled(Machine);

        options.Size.ShouldBe(new PixelSize(1280, 720));
        options.Gpu.ShouldBeFalse();
        options.FrameRate.ShouldBe(30);
        options.LatencyMilliseconds.ShouldBe(60);
        options.Volume.ShouldBe(1f);
        options.Mute.ShouldBeFalse();
        options.From.ShouldBe(0);
        options.For.ShouldBeNull();
        options.Loop.ShouldBeNull();
        options.Window.ShouldBeNull();
        options.Patch.ShouldBeNull();
        options.Preset.ShouldBeNull();
        options.Transport.ShouldBe(TransportEdge.Bottom);
    }

    [Fact]
    public void Every_flag_overrides_what_the_settings_say()
    {
        var options = Settled(
            Machine,
            "nebula.fbk",
            "--size", "640x360",
            "--fps", "0",
            "--gpu",
            "--no-video",
            "--window", "800x450",
            "--maximized",
            "--no-audio",
            "--volume", "0.25",
            "--mute",
            "--latency", "20",
            "--from", "12.5",
            "--paused",
            "--for", "8",
            "--loop", "4",
            "--background",
            "--no-overlay",
            "--stats",
            "--transport", "top",
            "--title", "hello",
            "--top",
            "--interpreted");

        options.Patch.ShouldBe("nebula.fbk");
        options.Size.ShouldBe(new PixelSize(640, 360));
        options.FrameRate.ShouldBe(0);
        options.Gpu.ShouldBeTrue();
        options.NoVideo.ShouldBeTrue();
        options.Window.ShouldBe(new PixelSize(800, 450));
        options.Maximized.ShouldBeTrue();
        options.NoAudio.ShouldBeTrue();
        options.Volume.ShouldBe(0.25f);
        options.Mute.ShouldBeTrue();
        options.LatencyMilliseconds.ShouldBe(20);
        options.From.ShouldBe(12.5);
        options.Paused.ShouldBeTrue();
        options.For.ShouldBe(8);
        options.Loop.ShouldBe(4);
        options.Background.ShouldBeTrue();
        options.NoOverlay.ShouldBeTrue();
        options.Stats.ShouldBeTrue();
        options.Transport.ShouldBe(TransportEdge.Top);
        options.Title.ShouldBe("hello");
        options.Top.ShouldBeTrue();
        options.Interpreted.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1080p", 1920, 1080)]
    [InlineData("720P", 1280, 720)]
    [InlineData("square", 1080, 1080)]
    [InlineData("portrait", 1080, 1920)]
    [InlineData("ultrawide", 2560, 1080)]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("333X222", 333, 222)]
    public void A_size_is_a_name_or_a_width_and_a_height(string text, int width, int height) =>
        Settled(Machine, "--size", text).Size.ShouldBe(new PixelSize(width, height));

    [Theory]
    [InlineData("huge")]
    [InlineData("0x0")]
    [InlineData("27000x27000")]
    public void A_size_it_cannot_draw_is_refused_before_anything_opens(string text)
    {
        var ran = Run(Machine, "--size", text);

        ran.Options.ShouldBeNull();
        ran.ParseErrors.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void The_transport_is_at_the_top_or_the_bottom_and_nowhere_else() =>
        Run(Machine, "--transport", "left").ParseErrors.ShouldBeGreaterThan(0);

    [Fact]
    public void The_renderers_are_each_other_s_opposite_and_one_is_enough()
    {
        Settled(new OutputSettings { Gpu = true }, "--cpu").Gpu.ShouldBeFalse();
        Settled(new OutputSettings { Gpu = false }, "--gpu").Gpu.ShouldBeTrue();

        var both = Run(Machine, "--gpu", "--cpu");

        both.Code.ShouldBe(Exit.Failed);
        both.Options.ShouldBeNull();
        both.Error.ShouldContain("--gpu");
        both.Error.ShouldContain("--cpu");
    }

    [Fact]
    public void A_patch_and_a_preset_are_two_answers_to_one_question()
    {
        var ran = Run(Machine, "nebula.fbk", "--preset", "Dub");

        ran.Code.ShouldBe(Exit.Failed);
        ran.Options.ShouldBeNull();
        ran.Error.ShouldContain("--preset");
    }

    [Fact]
    public void Hidden_plays_with_no_picture_and_refuses_what_shapes_a_window()
    {
        var hidden = Settled(Machine, "--hidden", "--for", "5");

        hidden.Hidden.ShouldBeTrue();
        hidden.Video.ShouldBeFalse();

        foreach (var flag in new[] { "--full-screen", "--maximized", "--top", "--no-overlay", "--stats" })
        {
            var ran = Run(Machine, "--hidden", flag);

            ran.Code.ShouldBe(Exit.Failed, flag);
            ran.Options.ShouldBeNull(flag);
            ran.Error.ShouldContain(flag);
        }

        Run(Machine, "--hidden", "--window", "800x450").Error.ShouldContain("--window");
    }

    [Fact]
    public void Full_screen_with_no_video_is_refused()
    {
        var ran = Run(Machine, "--full-screen", "--no-video");

        ran.Code.ShouldBe(Exit.Failed);
        ran.Options.ShouldBeNull();
        ran.Error.ShouldContain("--no-video");
    }

    [Fact]
    public void Full_screen_is_refused_for_a_patch_with_no_picture()
    {
        var error = new StringWriter();
        var library = new PresetLibrary(Path.Combine(Path.GetTempPath(), "flyback-viewer-none-" + Guid.NewGuid().ToString("N")));
        var path = Path.Combine(Path.GetTempPath(), $"flyback-viewer-heard-{Guid.NewGuid():N}.{PatchIO.FileExtension}");

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var osc = builder.Add("osc.sine", 0, 0);
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        File.WriteAllText(path, PatchIO.ToJson(builder.Wire(osc, 0, speaker, NodeCatalog.OutputLeftPort).Patch));

        try
        {
            ViewerSource.Resolve(new ViewerOptions { Patch = path }, Machine, PluginCatalog.Empty, library, error)
                .ShouldNotBeNull();

            ViewerSource.Resolve(new ViewerOptions { Patch = path, FullScreen = true }, Machine, PluginCatalog.Empty, library, error)
                .ShouldBeNull();

            error.ToString().ShouldContain("--full-screen");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("--volume", "1.5")]
    [InlineData("--volume", "-0.1")]
    [InlineData("--latency", "1")]
    [InlineData("--latency", "9000")]
    [InlineData("--from", "-1")]
    [InlineData("--for", "0")]
    [InlineData("--loop", "-3")]
    [InlineData("--fps", "-1")]
    public void A_number_out_of_its_range_is_refused(string flag, string value)
    {
        var ran = Run(Machine, flag, value);

        ran.Options.ShouldBeNull();
        ran.ParseErrors.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_settings_flag_is_found_before_the_command_exists()
    {
        ViewerArguments.SettingsPath(["a.fbk", "--settings", "other.json"]).ShouldBe("other.json");
        ViewerArguments.SettingsPath(["--settings=other.json"]).ShouldBe("other.json");
        ViewerArguments.SettingsPath(["a.fbk"]).ShouldBeNull();
        ViewerArguments.SettingsPath(["--settings"]).ShouldBeNull();
    }

    [Fact]
    public void A_preset_name_nothing_has_says_so_and_plays_nothing()
    {
        var error = new StringWriter();
        var library = new PresetLibrary(Path.Combine(Path.GetTempPath(), "flyback-viewer-none-" + Guid.NewGuid().ToString("N")));

        var source = ViewerSource.Resolve(
            new ViewerOptions { Preset = "No such thing" }, Machine, PluginCatalog.Empty, library, error);

        source.ShouldBeNull();
        error.ToString().ShouldContain("No such thing");
    }

    [Fact]
    public void A_path_that_is_not_there_says_so_and_plays_nothing()
    {
        var error = new StringWriter();
        var library = new PresetLibrary(Path.Combine(Path.GetTempPath(), "flyback-viewer-none-" + Guid.NewGuid().ToString("N")));

        var source = ViewerSource.Resolve(
            new ViewerOptions { Patch = Path.Combine(Path.GetTempPath(), "flyback-nothing-here.fbk") },
            Machine, PluginCatalog.Empty, library, error);

        source.ShouldBeNull();
        error.ToString().ShouldContain("no such file");
    }
}
