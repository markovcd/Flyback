using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Sample module: a recording played by reading it at a position, and the
/// one thing in a patch that is a reference to something outside the file.
/// </summary>
/// <remarks>
/// Two halves worth keeping apart. What it plays is ordinary arithmetic and is
/// checked against the clip. What happens when the file is not there is the
/// whole cost of a patch naming its audio rather than carrying it, and is
/// checked hardest — a sample that has gone must be said out loud, by name, on
/// both sinks, and must still compile to something that renders.
/// </remarks>
public class SampleTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-samples").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>A clip of a known shape: a ramp from 0 to 1 over one second.</summary>
    private string Ramp(string name = "ramp.wav", int rate = 1000)
    {
        var samples = new float[rate];
        for (var i = 0; i < rate; i++) samples[i] = i / (float)rate;

        var path = Path.Combine(folder, name);
        WavWriter.Write(path, samples, rate, 1);

        return path;
    }

    private SampleLibrary Library() => new() { Beside = folder };

    /// <summary>
    /// A patch of one Sample held at a position, sent to the speakers.
    /// </summary>
    /// <remarks>
    /// The position is wired from a Value rather than set on the socket, because
    /// 'in' is a domain and a domain is normalled to Time (ADR-0050) — a knob on
    /// one is not read. That is the module working: a player dropped on a canvas
    /// runs on the clock and plays once. It does mean a test that wants it held
    /// still has to say so, the same way a patch would.
    /// </remarks>
    private static (Patch Patch, NodeInstance Player) Playing(
        string path,
        float at = 0f,
        float level = 1f)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var position = b.Add("value", -200, 0, (0, at));
        var player = b.Add(NodeCatalog.SampleTypeId, 0, 0, (1, level));
        SampleExtra.Set(player, path);

        var sink = b.Add(NodeCatalog.OutputTypeId, 200, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(position, 0, player, 0)
         .Wire(player, 0, sink, NodeCatalog.OutputLeftPort);

        return (b.Patch, player);
    }

    private static double Heard(CompileResult compiled)
    {
        var registers = compiled.Program.AllocateRegisters();
        compiled.Program.Evaluate(0f, 0f, 0f, registers, default);

        return registers[compiled.Program.OutputBase];
    }

    [Fact]
    public void It_plays_the_file_at_the_position_it_is_given()
    {
        var path = Ramp();
        var library = Library();

        // A ramp over one second, so halfway in is half way up.
        foreach (var at in new[] { 0.1f, 0.25f, 0.5f, 0.9f })
        {
            var (patch, _) = Playing(path, at);
            Heard(patch.CompileForAudio(NodeCatalog.BuiltIn, library)).ShouldBe(at, 0.01);
        }
    }

    /// <summary>
    /// Silence either side rather than a clamp or a wrap. Running off the end is
    /// how a one-shot ends, and holding the last sample for ever would be a
    /// click followed by DC.
    /// </summary>
    [Theory]
    [InlineData(-0.5f)]
    [InlineData(-0.001f)]
    [InlineData(1.5f)]
    [InlineData(100f)]
    public void Off_either_end_is_silence(float at)
    {
        var (patch, _) = Playing(Ramp(), at);

        Heard(patch.CompileForAudio(NodeCatalog.BuiltIn, Library())).ShouldBe(0d);
    }

    /// <summary>
    /// The second output, so a patch can loop or scale without being told how
    /// long the file is. A constant the compiler works out, not a signal.
    /// </summary>
    [Fact]
    public void It_says_how_long_the_file_is()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var player = b.Add(NodeCatalog.SampleTypeId, 0, 0);
        SampleExtra.Set(player, Ramp("half.wav", 500));

        var sink = b.Add(NodeCatalog.OutputTypeId, 200, 0, (NodeCatalog.OutputGainPort, 1f));
        b.Wire(player, 1, sink, NodeCatalog.OutputLeftPort);

        // 500 samples at 500 a second.
        Heard(b.Patch.CompileForAudio(NodeCatalog.BuiltIn, Library())).ShouldBe(1d, 1e-5);
    }

    [Fact]
    public void Level_scales_what_comes_out()
    {
        var (patch, _) = Playing(Ramp(), at: 0.5f, level: 0.25f);

        Heard(patch.CompileForAudio(NodeCatalog.BuiltIn, Library())).ShouldBe(0.125, 0.01);
    }

    // --- the trigger ----------------------------------------------------------

    /// <summary>
    /// A player driven by the clock and fired by a gate, stepped in order the
    /// way the renderer steps it — the trigger remembers, so it cannot be
    /// sampled at scattered moments.
    /// </summary>
    private sealed class Triggered
    {
        private const int Rate = 4_000;

        private readonly CompiledPatch program;
        private readonly double[] registers;
        private readonly DelayState memory;

        /// <param name="fall">
        /// A clip that falls from 1 to 0 over its length, so the level read back
        /// says exactly where the playhead is: 1 is the very start and 0 the end.
        /// That is what makes "it went back to the beginning" a thing a test can
        /// see rather than infer.
        /// </param>
        /// <param name="triggerHz"></param>
        /// <param name="width"></param>
        public Triggered(string fall, float triggerHz, float width = 0.05f)
        {
            var b = new PatchBuilder(NodeCatalog.BuiltIn);

            var player = b.Add(NodeCatalog.SampleTypeId, 0, 0);
            SampleExtra.Set(player, fall);

            var sink = b.Add(NodeCatalog.OutputTypeId, 300, 0, (NodeCatalog.OutputGainPort, 1f));
            b.Wire(player, 0, sink, NodeCatalog.OutputLeftPort);

            if (triggerHz > 0)
            {
                var pulse = b.Add("osc.pulse", -400, 200, (1, triggerHz), (3, width));
                b.Wire(pulse, 0, player, 2);
            }

            var library = new SampleLibrary { Beside = Path.GetDirectoryName(fall) };

            program = b.Patch.CompileForAudio(NodeCatalog.BuiltIn, library).Program;
            registers = program.AllocateRegisters();
            memory = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        }

        /// <summary>Steps to <paramref name="seconds"/> and answers what is coming out there.</summary>
        public double At(double seconds)
        {
            var last = 0d;

            while (Evaluations <= seconds * Rate)
            {
                program.Evaluate(0f, 0f, (float)(Evaluations / (double)Rate), registers, default, memory);
                last = registers[program.OutputBase];
                Evaluations++;
            }

            return last;
        }

        private int Evaluations { get; set; }
    }

    /// <summary>A clip that falls from 1 to 0 over <paramref name="seconds"/>.</summary>
    private string Falling(double seconds, string name = "fall.wav")
    {
        const int rate = 4_000;

        var pcm = new float[(int)(rate * seconds)];
        for (var i = 0; i < pcm.Length; i++) pcm[i] = 1f - i / (float)pcm.Length;

        var path = Path.Combine(folder, name);
        WavWriter.Write(path, pcm, rate, 1);

        return path;
    }

    /// <summary>
    /// The whole of what the socket is for: each trigger starts the clip again
    /// from its beginning, at the speed it was recorded.
    /// </summary>
    [Fact]
    public void A_trigger_starts_the_clip_from_the_beginning()
    {
        var rig = new Triggered(Falling(0.8), triggerHz: 0.8f);

        // Just after the first edge, which the pulse puts a twentieth of a
        // cycle in — the very start of the clip.
        rig.At(0.08).ShouldBe(1d, 0.05);

        // Halfway through the clip, halfway down the ramp.
        rig.At(0.47).ShouldBe(0.5, 0.05);

        // Past the end and silent, because nothing has retriggered it.
        rig.At(1.0).ShouldBe(0d);

        // And the next edge starts it over.
        rig.At(1.33).ShouldBe(1d, 0.05);
    }

    /// <summary>
    /// The half that was asked for by name: an edge arriving while the clip is
    /// still sounding cuts it short rather than being ignored or layered.
    /// </summary>
    [Fact]
    public void A_trigger_arriving_mid_clip_starts_it_again()
    {
        // Triggered twice a second, with a clip that runs for eight tenths — so
        // every edge lands three tenths before it would have finished.
        var rig = new Triggered(Falling(0.8), triggerHz: 2f);

        rig.At(0.47).ShouldBe(0.5, 0.06, "should be halfway down the ramp");
        rig.At(0.53).ShouldBeGreaterThan(0.9, "the edge should have put it back to the start");

        // And it never runs out, because it never gets to the end.
        rig.At(3.0).ShouldBeGreaterThan(0.4);
    }

    /// <summary>
    /// An edge and not a level, so the width of the trigger does not matter —
    /// a spike one evaluation wide fires it exactly as a long gate does.
    /// </summary>
    [Fact]
    public void A_trigger_of_any_width_fires_it()
    {
        var spike = new Triggered(Falling(0.8, "spike.wav"), triggerHz: 2f, width: 0.001f);

        spike.At(0.53).ShouldBeGreaterThan(0.9);
        spike.At(1.03).ShouldBeGreaterThan(0.9);
    }

    /// <summary>
    /// Where the clip was started from is a reading of the clock, not a signal,
    /// so it is written to a cell that is not held to the rails a signal is —
    /// see Emitter.ClockWrite. Bounded at sixteen, a player would stop working
    /// sixteen seconds into a session, which is the sort of thing nobody finds
    /// until a set is half an hour old.
    /// </summary>
    [Fact]
    public void A_trigger_still_works_long_after_a_signal_would_have_run_out_of_room()
    {
        var rig = new Triggered(Falling(0.8), triggerHz: 0.8f);

        // Well past sixteen seconds, and past a hundred.
        rig.At(120.08).ShouldBe(1d, 0.06);
        rig.At(120.47).ShouldBe(0.5, 0.06);
    }

    /// <summary>
    /// Nothing patched into it leaves the module exactly as it was: the clip is
    /// read at 'in' itself, which is the clock, so it plays once and stops.
    /// </summary>
    [Fact]
    public void With_no_trigger_it_is_the_player_it_always_was()
    {
        var rig = new Triggered(Falling(0.8), triggerHz: 0f);

        rig.At(0.02).ShouldBeGreaterThan(0.9);
        rig.At(0.4).ShouldBe(0.5, 0.05);
        rig.At(1.0).ShouldBe(0d);
        rig.At(5.0).ShouldBe(0d);
    }

    // --- the file, and what happens without it -------------------------------

    /// <summary>
    /// The cost of a patch naming its audio rather than carrying it, and the one
    /// thing that must not be passed over quietly.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_there_is_reported_by_name()
    {
        var missing = Path.Combine(folder, "gone.wav");
        var (patch, _) = Playing(missing);

        var compiled = patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        compiled.HasErrors.ShouldBeTrue();
        compiled.Issues.ShouldContain(i => i.Message.Contains("gone.wav"));
        compiled.Issues.ShouldContain(i => i.Message.Contains("no file there"));
    }

    /// <summary>
    /// A patch is still a patch. What compiles is silence where the recording
    /// would have been, so the editor goes on drawing and the rest of the sound
    /// goes on playing while the file is found again.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_there_still_compiles_to_something_that_runs()
    {
        var (patch, _) = Playing(Path.Combine(folder, "gone.wav"));

        Heard(patch.CompileForAudio(NodeCatalog.BuiltIn, Library())).ShouldBe(0d);
    }

    /// <summary>
    /// Said on both compilations. A missing file is a fault in the patch
    /// whichever half of it you are looking at, and the status bar shows whatever
    /// the picture's compile had to say.
    /// </summary>
    [Fact]
    public void A_missing_file_is_reported_to_the_eye_as_well_as_the_ear()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var player = b.Add(NodeCatalog.SampleTypeId, 0, 0);
        SampleExtra.Set(player, Path.Combine(folder, "gone.wav"));

        var sink = b.Add(NodeCatalog.OutputTypeId, 200, 0);
        b.Wire(player, 0, sink, NodeCatalog.OutputColorPort);

        b.Patch.CompileForVideo(NodeCatalog.BuiltIn, Library())
            .Issues.ShouldContain(i => i.Message.Contains("gone.wav"));
    }

    /// <summary>
    /// A module nobody has chosen a file for yet is not broken, it is unfinished
    /// — so it is a warning rather than an error, and a patch carrying one can
    /// still be rendered and offered.
    /// </summary>
    [Fact]
    public void A_player_with_no_file_chosen_is_remarked_on_rather_than_refused()
    {
        var (patch, _) = Playing(string.Empty);

        var compiled = patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        compiled.HasErrors.ShouldBeFalse();
        compiled.Issues.ShouldContain(i => i.Message.Contains("no sound file chosen"));
    }

    /// <summary>
    /// The complaint names the module, so a patch with four players in it says
    /// which one lost its file.
    /// </summary>
    [Fact]
    public void The_complaint_names_the_module_it_is_about()
    {
        var (patch, player) = Playing(Path.Combine(folder, "gone.wav"));
        player.Rename(NodeCatalog.BuiltIn.Require(NodeCatalog.SampleTypeId), "Snare");

        var compiled = patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        compiled.Issues.ShouldContain(i => i.Message.Contains("Snare") && i.NodeId == player.Id);
    }

    /// <summary>
    /// The eye is given the clip as well as the ear, and the Probe is why.
    /// </summary>
    /// <remarks>
    /// A Probe is a video program (ADR-0040), so the screen has to be able to
    /// read a clip too — otherwise pointing one at a sample would chart a flat
    /// line, and the one tool for seeing what a signal does could not see the
    /// one signal that comes from outside the patch.
    /// <para>
    /// The backends are kept in step in the shell, by drawing a program that
    /// reads a clip on the processor. What is checked here is the half that
    /// makes that necessary and worthwhile: the interpreter reads the recording
    /// wherever it is asked to.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_screen_reads_the_clip_too_so_a_probe_can_chart_it()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var position = b.Add("value", -200, 0, (0, 0.5f));
        var player = b.Add(NodeCatalog.SampleTypeId, 0, 0);
        SampleExtra.Set(player, Ramp());

        var sink = b.Add(NodeCatalog.OutputTypeId, 200, 0);

        b.Wire(position, 0, player, 0)
         .Wire(player, 0, sink, NodeCatalog.OutputColorPort);

        var compiled = b.Patch.CompileForVideo(NodeCatalog.BuiltIn, Library());

        compiled.Program.Tables.Count.ShouldBe(1);
        Heard(compiled).ShouldBe(0.5, 0.01);
    }

    /// <summary>
    /// A trigger means nothing where there is no memory, and the screen has
    /// none — so what the eye gets is the module without one: the clip read at
    /// 'in'.
    /// </summary>
    /// <remarks>
    /// Reading the trigger cell at every pixel instead would read nought
    /// everywhere, since there is no memory on the screen — so every pixel would
    /// look like a rising edge, restarting the clip at that pixel's own position
    /// and reading it at its first sample, which on a drum is silence. That is
    /// neither what the speakers do nor a memoryless reading of the patch, but a
    /// third thing that happens to look exactly like the module not working.
    /// </remarks>
    [Fact]
    public void A_trigger_is_ignored_on_the_screen_rather_than_flattening_the_clip()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var held = b.Add("value", -400, 100, (0, 1f));
        var position = b.Add("value", -400, 0, (0, 0.5f));

        var player = b.Add(NodeCatalog.SampleTypeId, 0, 0);
        SampleExtra.Set(player, Ramp());

        var sink = b.Add(NodeCatalog.OutputTypeId, 200, 0);

        b.Wire(position, 0, player, 0)
         .Wire(held, 0, player, 2)
         .Wire(player, 0, sink, NodeCatalog.OutputColorPort);

        // Halfway up the ramp, exactly as it reads with no trigger at all —
        // rather than the nought a collapsed position would give.
        Heard(b.Patch.CompileForVideo(NodeCatalog.BuiltIn, Library())).ShouldBe(0.5, 0.01);
    }

    /// <summary>
    /// And a Probe rooted at one charts it, which is the case the whole rule was
    /// changed for. The Probe sweeps time across the picture, so a column away
    /// from the middle is the clip at a different moment — which is what makes
    /// the chart a waveform rather than a flat line.
    /// </summary>
    [Fact]
    public void A_probe_pointed_at_a_player_charts_the_recording()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var player = b.Add(NodeCatalog.SampleTypeId, 0, 0);
        SampleExtra.Set(player, Ramp());

        var probe = b.Add(NodeCatalog.ProbeTypeId, 200, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0);

        b.Wire(player, 0, probe, 0)
         .Wire(probe, 0, sink, NodeCatalog.OutputColorPort);

        var compiled = b.Patch.CompileForProbe(probe.Id, NodeCatalog.BuiltIn, Library());

        compiled.HasErrors.ShouldBeFalse();
        compiled.Program.Tables.Count.ShouldBe(1, "the chart has nothing to draw without the clip");
    }

    /// <summary>
    /// A Sample the screen never reaches puts no table in the video program at
    /// all, so nothing about the picture changes for a patch that only plays
    /// one — which is what keeps the processor fallback rare.
    /// </summary>
    [Fact]
    public void A_player_only_the_speakers_reach_leaves_the_picture_alone()
    {
        var (patch, _) = Playing(Ramp(), at: 0.5f);

        patch.CompileForVideo(NodeCatalog.BuiltIn, Library()).Program.Tables.ShouldBeEmpty();
        patch.CompileForAudio(NodeCatalog.BuiltIn, Library()).Program.Tables.Count.ShouldBe(1);
    }

    /// <summary>
    /// Compiling with nothing to look files up in is not an error either — it is
    /// what every test and every tool that does not care about audio does.
    /// </summary>
    [Fact]
    public void With_no_library_at_all_it_is_silence_and_a_complaint()
    {
        var (patch, _) = Playing(Ramp());

        var compiled = patch.CompileForAudio(NodeCatalog.BuiltIn);

        Heard(compiled).ShouldBe(0d);
        compiled.HasErrors.ShouldBeTrue();
    }

    // --- the library ---------------------------------------------------------

    [Fact]
    public void The_library_reads_a_file_once_however_often_it_is_asked()
    {
        var path = Ramp();
        var library = Library();

        var first = library.Find(path);
        var again = library.Find(path);

        first.ShouldNotBeNull();
        again.ShouldBeSameAs(first);
        library.Count.ShouldBe(1);
    }

    /// <summary>
    /// Two players on one file share a table, the way two modules on one number
    /// share a literal.
    /// </summary>
    [Fact]
    public void Two_players_on_one_file_share_one_table()
    {
        var path = Ramp();
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var early = b.Add("value", -200, 0, (0, 0.25f));
        var late = b.Add("value", -200, 100, (0, 0.75f));

        var first = b.Add(NodeCatalog.SampleTypeId, 0, 0);
        var second = b.Add(NodeCatalog.SampleTypeId, 0, 100);

        SampleExtra.Set(first, path);
        SampleExtra.Set(second, path);

        var mix = b.Add("math.add", 200, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(early, 0, first, 0)
         .Wire(late, 0, second, 0)
         .Wire(first, 0, mix, 0).Wire(second, 0, mix, 1).Wire(mix, 0, sink, NodeCatalog.OutputLeftPort);

        var compiled = b.Patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        compiled.Program.Tables.Count.ShouldBe(1);
        Heard(compiled).ShouldBe(1d, 0.02);
    }

    /// <summary>
    /// A relative path is measured from wherever the patch lives, which is what
    /// lets a patch and its samples be copied somewhere else together.
    /// </summary>
    [Fact]
    public void A_relative_path_is_found_beside_the_patch()
    {
        Ramp("beside.wav");

        var (patch, _) = Playing("beside.wav", 0.5f);
        var compiled = patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        compiled.HasErrors.ShouldBeFalse();
        Heard(compiled).ShouldBe(0.5, 0.01);
    }

    /// <summary>
    /// The same relative path means a different file once the patch moves, so
    /// what was known about one is dropped.
    /// </summary>
    [Fact]
    public void Moving_the_patch_forgets_what_was_found_beside_the_old_one()
    {
        Ramp("beside.wav");

        var library = Library();
        library.Find("beside.wav").ShouldNotBeNull();

        library.Beside = Path.GetTempPath();
        library.Find("beside.wav").ShouldBeNull();
    }

    /// <summary>
    /// A file that arrives after the complaint about it should be picked up,
    /// which is what makes fixing one a matter of putting it back rather than
    /// reopening the patch.
    /// </summary>
    [Fact]
    public void Forgetting_a_path_gives_the_file_another_chance()
    {
        var library = Library();

        library.Find("late.wav").ShouldBeNull();
        Ramp("late.wav");

        library.Find("late.wav").ShouldBeNull("a failure is remembered too");

        library.Forget("late.wav");
        library.Find("late.wav").ShouldNotBeNull();
    }

    [Fact]
    public void The_library_says_why_a_file_could_not_be_had()
    {
        var library = Library();

        library.Explain("gone.wav").ShouldContain("no file there");

        File.WriteAllText(Path.Combine(folder, "prose.wav"), "this is not a wave");
        library.Explain("prose.wav").ShouldContain("not a WAV");
    }
}
