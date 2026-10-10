using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- listening ----------------------------------------------------------

    [Fact]
    public void Listening_is_offered_only_when_the_model_can_hear()
    {
        Bench().Tools.Select(t => t.Name).ShouldContain("listen");
        Bench(hearing: Listener.Itself).Tools.Select(t => t.Name).ShouldContain("listen");
        Bench(hearing: Listener.None).Tools.Select(t => t.Name).ShouldNotContain("listen");
    }

    /// <summary>
    /// The tool says whose ear answers it, because the two answers are not the
    /// same kind of thing. A borrowed one is somebody else's opinion, told
    /// nothing so that it can disagree; the model's own is its impression of a
    /// patch it built and hoped for. A model told the wrong one of those credits
    /// its own ears to a listener that was never there.
    /// </summary>
    [Fact]
    public void The_listen_tool_says_who_is_going_to_hear_it()
    {
        var borrowed = Listen(Bench(hearing: Listener.Another));
        var own = Listen(Bench(hearing: Listener.Itself));

        borrowed.Description.ShouldContain("a model that can hear");
        own.Description.ShouldContain("plays it to you");

        // And the note that goes with it: withholding it is the borrowed
        // arrangement's one safeguard, and there is nobody to withhold it from
        // when the model hears the clip itself.
        borrowed.Schema.ShouldContain("deliberately not passed on");
        own.Schema.ShouldContain("Nobody else reads it");
    }

    /// <summary>
    /// And so does the briefing, which is the only place a model is told what it
    /// can check.
    /// </summary>
    [Fact]
    public void The_briefing_says_whose_ear_it_is()
    {
        Bench(hearing: Listener.None).Briefing.ShouldContain("You cannot hear");
        Bench(hearing: Listener.Another).Briefing.ShouldContain("though it is not yours");
        Bench(hearing: Listener.Itself).Briefing.ShouldContain("You can hear.");
    }

    /// <summary>
    /// All three are still deterministic, which is what the prefix cache is
    /// bought with — a briefing that varies between turns pays to write the
    /// cache again on every one of them.
    /// </summary>
    [Fact]
    public void Every_version_of_the_briefing_is_the_same_text_every_time()
    {
        foreach (var who in Enum.GetValues<Listener>())
            Bench(hearing: who).Briefing.ShouldBe(Bench(hearing: who).Briefing);
    }

    private static PatchTool Listen(PatchWorkbench bench) =>
        bench.Tools.Single(t => t.Name == "listen");

    /// <summary>
    /// Off by default where sight is on, because a sound reaches only the few
    /// models built to take one and a picture reaches all of them.
    /// </summary>
    [Fact]
    public void Hearing_is_not_assumed_the_way_sight_is()
    {
        var tools = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch()).Tools.Select(t => t.Name).ToArray();

        tools.ShouldContain("render");
        tools.ShouldNotContain("listen");
    }

    [Fact]
    public async Task A_listen_is_a_wav_of_the_length_it_says()
    {
        var heard = await Call(await Heard(), "listen", """{"seconds":1}""");

        heard.Ok.ShouldBeTrue(heard.Text);
        heard.Wav.ShouldNotBeNull();
        heard.Png.ShouldBeNull();

        Riff(heard.Wav).ShouldBe("RIFF");
        Format(heard.Wav).ShouldBe("WAVE");
        Channels(heard.Wav).ShouldBe(2);
        Rate(heard.Wav).ShouldBe(24_000);

        // One second, stereo, two bytes a sample.
        DataBytes(heard.Wav).ShouldBe(24_000 * 2 * 2);
    }

    /// <summary>
    /// The caption is the only thing said about a payload the model hears rather
    /// than reads, so the number in it has to be the number in the file. A sine
    /// through the Output's default volume of 0.5 peaks at half of full scale,
    /// which is −6 dBFS.
    /// </summary>
    [Fact]
    public async Task What_it_is_told_about_the_level_is_the_level_it_is_sent()
    {
        var heard = await Call(await Heard(), "listen", """{"seconds":0.5}""");

        heard.Text.ShouldContain("Peak -6");
        heard.Text.ShouldContain("dBFS");
    }

    /// <summary>
    /// The counterpart of the black-frame rule. A patch built for the screen is
    /// silent on purpose and the compiler does not remark on it, so playing
    /// one would hand back two seconds of nothing — which reads as a broken tool
    /// rather than as a patch that was never meant to make a sound.
    /// </summary>
    [Fact]
    public async Task A_patch_with_no_sound_is_not_played_silence_at_it()
    {
        var heard = await Call(await Lit(0.5f), "listen");

        heard.Ok.ShouldBeFalse();
        heard.Wav.ShouldBeNull();
        heard.Text.ShouldContain("'left'");
    }

    /// <summary>
    /// A patch that is wired, compiles without a word, and makes no sound. The
    /// compiler catches the loud version of this — an oscillator with nothing at
    /// all on its 'in' is a warning — so what is left for the ear is the quiet
    /// version: everything correct, and the volume at zero. Playing the model half
    /// a second of nothing would tell it far less than the sentence does, and
    /// costs a payload to say it.
    /// </summary>
    [Fact]
    public async Task A_patch_that_compiles_cleanly_and_makes_no_sound_comes_back_as_a_sentence()
    {
        var bench = await Heard();

        var turned = await Call(bench, "set_knobs", """
            {"handle":"output1","knobs":[{"port":"volume","value":0}]}
            """);
        turned.Ok.ShouldBeTrue(turned.Text);

        var heard = await Call(bench, "listen", """{"seconds":0.5}""");

        heard.Wav.ShouldBeNull();
        heard.Text.ShouldContain("silence");
        heard.Text.ShouldContain("volume");
    }

    /// <summary>
    /// A constant reaching 'left' thumps once as the DC blocker settles and is then
    /// nothing, so it is silence from the very start, not a clip to play.
    /// </summary>
    [Fact]
    public async Task A_constant_reaching_the_speakers_is_silence_from_the_start()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"audio.note","handle":"pitch1"}""");
        await Call(bench, "connect", """{"from":"pitch1","from_port":"hz","to":"output1","to_port":"left"}""");

        var heard = await Call(bench, "listen", """{"from":0,"seconds":2}""");

        heard.Wav.ShouldBeNull();
        heard.Text.ShouldContain("silence");
    }

    /// <summary>
    /// The other half of the same rule, and the one the compiler does see:
    /// anything it has to say is enough to stop this. Rendering it anyway would
    /// spend a payload on a recording of a patch already diagnosed in words.
    /// </summary>
    [Fact]
    public async Task A_fault_the_compiler_can_see_is_answered_by_it_and_not_by_the_ear()
    {
        var bench = Bench();

        await Call(bench, "add_module", $$"""{"type_id":"{{NodeCatalog.SampleTypeId}}","handle":"clip1"}""");
        await Call(bench, "set_sample", """{"handle":"clip1","path":"gone.wav"}""");
        var wired = await Call(bench, "connect", """{"from":"clip1","from_port":"out","to":"output1","to_port":"left"}""");
        wired.Ok.ShouldBeTrue(wired.Text);

        var heard = await Call(bench, "listen", """{"seconds":0.5}""");

        heard.Ok.ShouldBeFalse();
        heard.Wav.ShouldBeNull();
        heard.Text.ShouldContain("gone.wav");
    }

    /// <summary>
    /// An oscillator with nothing patched into it is a sound that can be
    /// listened to: the socket carries the clock without a wire, so there is
    /// nothing for the compiler to catch and nothing to keep the ear from
    /// being asked.
    /// </summary>
    [Fact]
    public async Task An_oscillator_with_nothing_driving_it_can_be_heard()
    {
        var bench = Bench();

        await Call(bench, "add_module", """
            {"type_id":"osc.sine","handle":"tone1","knobs":[{"port":"freq","value":440}]}
            """);
        await Call(bench, "connect", """{"from":"tone1","to":"output1","to_port":"left"}""");

        var heard = await Call(bench, "listen", """{"seconds":0.5}""");

        heard.Ok.ShouldBeTrue(heard.Text);
        heard.Wav.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_patch_that_does_not_compile_cannot_be_listened_to()
    {
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"osc.sine"}""");
        var heard = await Call(bench, "listen");

        heard.Ok.ShouldBeFalse();
        heard.Wav.ShouldBeNull();
    }

    /// <summary>
    /// What one call may spend is the workbench's to decide, not the model's:
    /// this goes into a request body as base64 and is paid for again on every
    /// turn that follows.
    /// </summary>
    [Fact]
    public async Task A_listen_longer_than_the_limit_is_cut_to_it()
    {
        var bench = new PatchWorkbench(
            NodeCatalog.BuiltIn,
            (await Heard()).Snapshot(),
            vision: false,
            hearing: Listener.Another,
            new WorkbenchLimits(LongestListen: 1d));

        var heard = await Call(bench, "listen", """{"seconds":30}""");

        heard.Wav.ShouldNotBeNull();
        DataBytes(heard.Wav).ShouldBe(24_000 * 2 * 2);
    }

    [Fact]
    public async Task Sound_can_start_anywhere_and_only_warms_up_just_before_it()
    {
        var late = await Call(await Heard(), "listen", """{"seconds":0.5,"from":3000}""");

        late.Wav.ShouldNotBeNull();
        late.Text.ShouldContain("from 3000s");
        late.Text.ShouldContain("warmed from 2996s");
    }

    /// <summary>
    /// The audio path is the one with a memory — delay lines and
    /// <c>feedback.unit</c>, per ADR-0027 — so a stretch starting at two seconds
    /// has to arrive with the seconds behind it having actually happened.
    /// A phasing patch is the cheapest thing that proves it: a delayed copy
    /// against the original cancels differently once the line has filled.
    /// </summary>
    [Fact]
    public async Task Sound_from_further_in_is_warmed_up_to_rather_than_sought_to()
    {
        var bench = await Heard();

        var start = await Call(bench, "listen", """{"seconds":0.5}""");
        var later = await Call(bench, "listen", """{"seconds":0.5,"from":2}""");

        start.Wav.ShouldNotBeNull();
        later.Wav.ShouldNotBeNull();

        // Same length, different samples: a 440 Hz sine two seconds along is not
        // where it was at zero, and a renderer that had been sought rather than
        // run would hand back the same buffer twice.
        DataBytes(later.Wav).ShouldBe(DataBytes(start.Wav));
        later.Wav.ShouldNotBe(start.Wav);
    }
}
