using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Mix;
using Shouldly;

namespace Flyback.Core.Tests.Mix;

/// <summary>A mix check listens to a patch for a few seconds and flags what usually goes wrong in a mix.</summary>
public class MixCheckTests
{
    private static MixReport Check(string source, double seconds = 1d)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return MixCheck.Take(load.Patch, new MixOptions(seconds), NodeCatalog.BuiltIn);
    }

    private static string[] Codes(MixReport report) => [.. report.Findings.Select(f => f.Code)];

    [Fact]
    public void A_patch_with_nothing_wired_to_the_speakers_has_no_sound_to_check()
    {
        var report = Check("rings(freq: 3) |> hsv(value: _) |> out.color");

        report.Findings.ShouldHaveSingleItem().Problem.ShouldBe(MixProblem.NoSound);
        report.Warned.ShouldBeFalse();
    }

    [Fact]
    public void Silence_at_the_speakers_is_a_warning()
    {
        var report = Check("sine(freq: 110) |> out.left\nout.volume = 0");

        report.Findings[0].Problem.ShouldBe(MixProblem.NoSound);
        report.Warned.ShouldBeTrue();
    }

    [Fact]
    public void A_sound_driven_past_full_scale_clips()
    {
        var report = Check("sine(freq: 110) * 3 |> out.left\nout.volume = 1");

        Codes(report).ShouldContain("clipping");
        report.Figures.Clips.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_constant_added_to_the_sound_is_a_dc_offset_on_the_module_that_adds_it()
    {
        var report = Check("let wet = sine(freq: 110, amp: 0.5, bias: 0.2)\nwet |> out.left\nout.volume = 1");

        var dc = report.Findings.Single(f => f.Problem == MixProblem.DcOffset);

        dc.Text.ShouldContain("wet");
        report.Figures.DcLeft!.Value.ShouldBe(0.2d, 0.01d);
    }

    [Fact]
    public void A_centered_sound_has_no_dc_offset()
    {
        Codes(Check("sine(freq: 110, amp: 0.5) |> out.left\nout.volume = 1")).ShouldNotContain("dc-offset");
    }

    [Fact]
    public void Bass_that_differs_between_left_and_right_is_a_wide_low_end()
    {
        var report = Check("let bass = sine(freq: 50, amp: 0.5)\nbass |> out.left\nbass * -1 |> out.right\nout.volume = 1");

        Codes(report).ShouldContain("wide-low-end");
    }

    [Fact]
    public void Bass_in_the_middle_is_not_wide()
    {
        var report = Check("let bass = sine(freq: 50, amp: 0.5)\nbass |> out.left\nbass |> out.right\nout.volume = 1");

        Codes(report).ShouldNotContain("wide-low-end");
        report.Figures.LowEndWidth.ShouldBeLessThan(MixCheck.Wide);
    }

    [Fact]
    public void A_whisper_is_too_quiet_and_a_full_scale_tone_too_loud()
    {
        Codes(Check("sine(freq: 1000, amp: 0.01) |> out.left\nout.volume = 1")).ShouldContain("too-quiet");
        Codes(Check("let tone = sine(freq: 1000)\ntone |> out.left\ntone |> out.right\nout.volume = 1")).ShouldContain("too-loud");
    }

    [Fact]
    public void An_oscillator_that_holds_still_on_the_way_to_the_output_is_named_as_silent()
    {
        var report = Check("let tone = sine(freq: 220, amp: 0.3)\nlet ghost = sine(freq: 330, amp: 0)\ntone + ghost |> out.left\nout.volume = 1");

        var silent = report.Findings.Single(f => f.Problem == MixProblem.SilentModule);

        silent.Text.ShouldContain("ghost");
        silent.Text.ShouldNotContain("tone");
        silent.Modules.Count.ShouldBe(1);
    }

    [Fact]
    public void A_song_without_a_limiter_wants_a_master_chain_and_a_sketch_is_only_told()
    {
        var song = Check("length 10\nsine(freq: 55, amp: 0.3) |> out.left\nout.volume = 1");
        var sketch = Check("sine(freq: 55, amp: 0.3) |> out.left\nout.volume = 1");

        song.Findings.Single(f => f.Problem == MixProblem.NoMasterChain).Severity.ShouldBe(MixSeverity.Warning);
        sketch.Findings.Single(f => f.Problem == MixProblem.NoMasterChain).Severity.ShouldBe(MixSeverity.Notice);
    }

    [Fact]
    public void A_song_with_nothing_under_60_hz_has_no_sub_and_one_with_a_sub_does()
    {
        Codes(Check("length 10\nsine(freq: 880, amp: 0.3) |> out.left\nout.volume = 1")).ShouldContain("no-sub");
        Codes(Check("length 10\nsine(freq: 45, amp: 0.3) + sine(freq: 880, amp: 0.1) |> out.left\nout.volume = 1")).ShouldNotContain("no-sub");
    }

    [Fact]
    public void Low_mids_that_outweigh_everything_else_in_a_song_are_muddy()
    {
        Codes(Check("length 10\nsine(freq: 300, amp: 0.3) + sine(freq: 55, amp: 0.05) |> out.left\nout.volume = 1")).ShouldContain("muddy");
    }

    [Fact]
    public void Warnings_come_before_notices()
    {
        var report = Check("length 10\nsine(freq: 880, amp: 3) |> out.left\nout.volume = 1");

        report.Findings.Select(f => f.Severity).ShouldBe(report.Findings.Select(f => f.Severity).OrderByDescending(s => s));
        report.Findings[0].Severity.ShouldBe(MixSeverity.Warning);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(MixOptions.MaxSeconds + 1d, 0d)]
    [InlineData(1d, -1d)]
    public void A_window_out_of_range_is_refused(double seconds, double from)
    {
        var load = PatchLanguage.Build("sine(freq: 110) |> out.left", NodeCatalog.BuiltIn);

        Should.Throw<ArgumentOutOfRangeException>(() => MixCheck.Take(load.Patch, new MixOptions(seconds, from), NodeCatalog.BuiltIn));
    }

    [Theory]
    [InlineData(MixProblem.NoMasterChain, "no-master-chain")]
    [InlineData(MixProblem.DcOffset, "dc-offset")]
    [InlineData(MixProblem.Clipping, "clipping")]
    public void A_finding_is_coded_in_kebab_case(MixProblem problem, string code) => MixWords.Code(problem).ShouldBe(code);
}
