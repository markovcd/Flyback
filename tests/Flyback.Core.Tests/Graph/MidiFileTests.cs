using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;
using static Flyback.Core.Tests.Graph.MidiBytes;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The MIDI File module: a .mid file read into notes, played by reading four tables
/// at the clock, and the one module besides a Sample that refers to a file.
/// </summary>
public sealed class MidiFileTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-midi").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static LoadedMidi Song(byte[] bytes)
    {
        var song = MidiFileReader.Read(bytes, out var fault);

        fault.ShouldBe(MidiFault.None);
        return song.ShouldNotBeNull();
    }

    // --- the reader ----------------------------------------------------------

    [Fact]
    public void A_quarter_note_at_120_beats_a_minute_lasts_half_a_second()
    {
        var song = Song(File(480, Track(On(0, 1, 60), Off(480, 1, 60))));

        song.Notes.ShouldHaveSingleItem().ShouldBe(new MidiNote(0f, 0.5f, 60, 100 / 127f, 1));
        song.Seconds.ShouldBe(0.5f);
    }

    [Fact]
    public void A_tempo_change_speeds_up_what_follows_it()
    {
        // Half a second for the first quarter, then 240 beats a minute: a quarter is a quarter second.
        var song = Song(File(480, Track(
            Tempo(0, 500_000), On(0, 1, 60), Off(480, 1, 60),
            Tempo(0, 250_000), On(0, 1, 62), Off(480, 1, 62))));

        var second = song.Notes.Single(n => n.Note == 62);

        second.Start.ShouldBe(0.5f, 1e-4f);
        second.End.ShouldBe(0.75f, 1e-4f);
    }

    [Fact]
    public void A_tempo_on_another_track_keeps_time_for_every_track()
    {
        var song = Song(File(480,
            Track(Tempo(0, 1_000_000)),
            Track(On(0, 1, 60), Off(480, 1, 60))));

        song.Notes.Single().End.ShouldBe(1f, 1e-4f);
    }

    [Fact]
    public void Of_two_tempo_changes_at_one_tick_the_later_in_the_file_wins()
    {
        // Enough changes that an unstable sort would reorder a pair: a second to the
        // quarter, then half that, at each of forty ticks.
        var changes = Enumerable.Range(0, 40)
            .SelectMany(tick => new[] { Tempo(tick == 0 ? 0 : 1, 1_000_000), Tempo(0, 500_000) });

        var song = Song(File(480, Track([.. changes, On(0, 1, 60), Off(480, 1, 60)])));

        song.Notes.Single().Start.ShouldBe(39 * 0.5f / 480, 1e-5f);
    }

    [Fact]
    public void A_note_on_of_velocity_nought_lets_the_note_go_and_running_status_is_read()
    {
        // 90 3C 64 | delta 480, running status: 3C 00 — a note-on of velocity 0.
        byte[] track = [0, 0x90, 60, 100, .. Delta(480), 60, 0];

        var song = Song(File(480, Track(track)));

        song.Notes.ShouldHaveSingleItem().End.ShouldBe(0.5f);
    }

    [Fact]
    public void A_note_never_let_go_ends_with_the_file()
    {
        var song = Song(File(480, Track(On(0, 1, 60), On(480, 1, 64), Off(480, 1, 64))));

        song.Notes.Single(n => n.Note == 60).End.ShouldBe(1f, 1e-4f);
    }

    [Fact]
    public void A_smpte_file_is_timed_by_the_second()
    {
        // 25 frames a second, 40 ticks a frame: a thousand ticks a second.
        byte[] header = [.. "MThd"u8.ToArray(), 0, 0, 0, 6, 0, 0, 0, 1, 0xE7, 40];
        byte[] track = Track(On(0, 1, 60), Off(1000, 1, 60));
        byte[] chunk = [.. "MTrk"u8.ToArray(), 0, 0, 0, (byte)track.Length, .. track];

        MidiFileReader.Read([.. header, .. chunk], out _).ShouldNotBeNull().Seconds.ShouldBe(1f, 1e-4f);
    }

    [Fact]
    public void A_truncated_track_gives_the_notes_it_had()
    {
        var whole = File(480, Track(On(0, 1, 60), Off(480, 1, 60), On(0, 1, 62), Off(480, 1, 62)));

        // Cut in the middle of the last event.
        var song = MidiFileReader.Read(whole[..^5], out var fault);

        song.ShouldNotBeNull();
        fault.ShouldBe(MidiFault.None);
        song.Notes.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MThd")]
    [InlineData("RIFF....WAVEfmt ")]
    public void A_file_that_is_not_midi_is_refused_by_name(string text)
    {
        MidiFileReader.Read(System.Text.Encoding.ASCII.GetBytes(text), out var fault).ShouldBeNull();

        fault.ShouldBe(MidiFault.NotMidi);
    }

    [Fact]
    public void A_file_with_no_notes_in_it_is_refused_by_name()
    {
        MidiFileReader.Read(File(480, Track(Tempo(0, 500_000))), out var fault).ShouldBeNull();

        fault.ShouldBe(MidiFault.Empty);
    }

    [Fact]
    public void A_track_that_claims_more_than_the_file_holds_is_read_as_far_as_it_goes()
    {
        var bytes = File(480, Track(On(0, 1, 60), Off(480, 1, 60)));

        // MTrk's length is the four bytes after it: claim four gigabytes.
        var at = Array.IndexOf(bytes, (byte)'k') + 1;
        bytes[at] = bytes[at + 1] = bytes[at + 2] = bytes[at + 3] = 0xFF;

        MidiFileReader.Read(bytes, out _).ShouldNotBeNull().Notes.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_variable_length_quantity_that_never_ends_stops_the_track()
    {
        byte[] track = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x90, 60, 100];

        MidiFileReader.Read(File(480, track), out var fault).ShouldBeNull();

        fault.ShouldBe(MidiFault.Empty);
    }

    [Fact]
    public void A_file_longer_than_anything_a_patch_could_play_is_refused_by_name()
    {
        // 0xFFFFFF microseconds to a quarter, and a note two hundred thousand quarters long.
        var song = MidiFileReader.Read(
            File(1, Track(Tempo(0, 0xFFFFFF), On(0, 1, 60), Off(200_000, 1, 60))), out var fault);

        song.ShouldBeNull();
        fault.ShouldBe(MidiFault.TooLong);
    }

    /// <summary>
    /// Each note is placed through every tempo change before it, so the changes are
    /// looked up rather than walked: a file under a megabyte must not take a minute.
    /// </summary>
    [Fact]
    public void A_file_of_many_tempo_changes_and_many_notes_reads_in_good_time()
    {
        var tempos = Enumerable.Range(0, 100_000).Select(_ => Tempo(1, 500_000));
        var notes = Enumerable.Range(0, 50_000).SelectMany(_ => new byte[] { 0, 60, 0, 0, 60, 100 });

        var bytes = File(96, Track([.. tempos]), Track(On(100_001, 1, 60), [.. notes]));

        var took = System.Diagnostics.Stopwatch.StartNew();
        var song = Song(bytes);
        took.Stop();

        song.Notes.Count.ShouldBe(50_001);

        // Tens of milliseconds alone. Walking the changes for each note took over a
        // minute, so ten seconds tells the two apart on a machine running every suite.
        took.Elapsed.TotalSeconds.ShouldBeLessThan(10d);
    }

    [Fact]
    public void A_file_bigger_than_the_limit_is_refused_before_it_is_read()
    {
        MidiFileReader.Read(new byte[MidiFileReader.MostBytes + 1], out var fault).ShouldBeNull();

        fault.ShouldBe(MidiFault.TooBig);
    }

    [Fact]
    public void A_path_that_is_not_there_is_refused_by_name()
    {
        MidiFileReader.Read(Path.Combine(folder, "nothing.mid"), out var fault).ShouldBeNull();

        fault.ShouldBe(MidiFault.Missing);
    }

    // --- the voices ----------------------------------------------------------

    private static float Read(LoadedSample table, double seconds) => (float)table.At(seconds);

    [Fact]
    public void One_voice_plays_the_newest_key_held_and_goes_back_to_the_one_under_it()
    {
        // C held a second; E struck at 0.25 and let go at 0.5.
        var song = Song(File(480, Track(
            On(0, 1, 60), On(240, 1, 64), Off(240, 1, 64), Off(960, 1, 60))));

        var line = song.Line(voice: 0, channel: 0);

        Read(line.Pitch, 0.1).ShouldBe(60f);
        Read(line.Pitch, 0.4).ShouldBe(64f);
        Read(line.Pitch, 0.8).ShouldBe(60f);
        Read(line.Gate, 0.4).ShouldBe(1f);
    }

    [Fact]
    public void The_gate_is_low_after_the_last_key_goes_and_the_pitch_stays()
    {
        var song = Song(File(480, Track(On(0, 1, 60), Off(480, 1, 60), On(960, 1, 72), Off(480, 1, 72))));
        var line = song.Line(0, 0);

        Read(line.Gate, 0.25).ShouldBe(1f);
        Read(line.Gate, 0.75).ShouldBe(0f);
        Read(line.Pitch, 0.75).ShouldBe(60f);
    }

    [Fact]
    public void The_gate_drops_for_an_instant_as_each_note_lands_so_a_legato_run_retriggers()
    {
        var song = Song(File(480, Track(On(0, 1, 60), On(480, 1, 62), Off(480, 1, 62), Off(0, 1, 60))));
        var line = song.Line(0, 0);

        Read(line.Gate, 0.25).ShouldBe(1f);
        Read(line.Gate, 0.5).ShouldBe(0f, 1e-6f);
        Read(line.Gate, 0.75).ShouldBe(1f);
    }

    [Fact]
    public void Each_note_struck_pulses_the_trigger_and_nothing_else_does()
    {
        var song = Song(File(480, Track(On(0, 1, 60), Off(480, 1, 60), On(0, 1, 62), Off(480, 1, 62))));
        var line = song.Line(0, 0);

        Read(line.Trigger, 0.002).ShouldBe(1f);
        Read(line.Trigger, 0.25).ShouldBe(0f);
        Read(line.Trigger, 0.502).ShouldBe(1f);
        Read(line.Trigger, 0.75).ShouldBe(0f);
    }

    [Fact]
    public void Velocity_follows_the_note_and_is_kept_after_it_is_let_go()
    {
        var song = Song(File(480, Track(On(0, 1, 60, 127), Off(480, 1, 60), On(480, 1, 62, 64), Off(480, 1, 62))));
        var line = song.Line(0, 0);

        Read(line.Velocity, 0.25).ShouldBe(1f);
        Read(line.Velocity, 0.75).ShouldBe(1f);
        Read(line.Velocity, 1.25).ShouldBe(64 / 127f, 1e-6f);
    }

    [Fact]
    public void Voices_share_out_the_notes_held_at_once_and_each_hears_only_its_own()
    {
        // A chord of C, E and G a second long.
        var song = Song(File(480, Track(
            On(0, 1, 60), On(0, 1, 64), On(0, 1, 67), Off(960, 1, 60), Off(0, 1, 64), Off(0, 1, 67))));

        Read(song.Line(1, 0).Pitch, 0.5).ShouldBe(60f);
        Read(song.Line(2, 0).Pitch, 0.5).ShouldBe(64f);
        Read(song.Line(3, 0).Pitch, 0.5).ShouldBe(67f);
        Read(song.Line(4, 0).Gate, 0.5).ShouldBe(0f);
    }

    [Fact]
    public void A_voice_not_wanted_for_a_note_is_free_for_the_next_one()
    {
        // C then, after it ends, E: both are voice 1.
        var song = Song(File(480, Track(On(0, 1, 60), Off(480, 1, 60), On(0, 1, 64), Off(480, 1, 64))));
        var line = song.Line(1, 0);

        Read(line.Pitch, 0.25).ShouldBe(60f);
        Read(line.Pitch, 0.75).ShouldBe(64f);
        Read(song.Line(2, 0).Gate, 0.75).ShouldBe(0f);
    }

    [Fact]
    public void A_channel_hears_only_its_own_notes()
    {
        var song = Song(File(480, Track(
            On(0, 1, 36), On(0, 2, 72), Off(480, 1, 36), Off(0, 2, 72))));

        Read(song.Line(0, 1).Pitch, 0.25).ShouldBe(36f);
        Read(song.Line(0, 2).Pitch, 0.25).ShouldBe(72f);
        song.Line(0, 3).Notes.ShouldBe(0);
    }

    [Fact]
    public void A_line_is_built_once()
    {
        var song = Song(File(480, Track(On(0, 1, 60), Off(480, 1, 60))));

        song.Line(0, 0).ShouldBeSameAs(song.Line(0, 0));
        song.Line(99, 99).ShouldBeSameAs(song.Line(8, 16));
    }

    // --- the module ----------------------------------------------------------

    private string Write(byte[] bytes, string name = "tune.mid")
    {
        var path = Path.Combine(folder, name);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A MIDI File sent to the speakers, one of its outputs on the left.</summary>
    private static (Patch Patch, NodeInstance Player) Playing(string path, int output, float volume = 1f)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var player = b.Add(NodeCatalog.MidiFileTypeId, 0, 0);
        MidiFileExtra.Set(player, path);

        var sink = b.Add(NodeCatalog.OutputTypeId, 200, 0, (NodeCatalog.OutputVolumePort, volume));
        b.Wire(player, output, sink, NodeCatalog.OutputLeftPort);

        return (b.Patch, player);
    }

    private static double Heard(CompileResult compiled, float at)
    {
        var registers = compiled.Program.AllocateRegisters();
        compiled.Program.Evaluate(0f, 0f, at, registers, default);

        return registers[compiled.Program.OutputBase];
    }

    private SampleLibrary Library() => new() { Beside = folder };

    [Fact]
    public void It_plays_the_file_on_the_clock()
    {
        var path = Write(File(480, Track(On(0, 1, 60), Off(480, 1, 60))));
        var gate = Playing(path, output: 1).Patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        Heard(gate, 0.25f).ShouldBe(1d);
        Heard(gate, 0.75f).ShouldBe(0d);
    }

    [Fact]
    public void It_says_how_long_the_file_is()
    {
        var path = Write(File(480, Track(On(0, 1, 60), Off(1920, 1, 60))));
        var length = Playing(path, output: 4).Patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        Heard(length, 0f).ShouldBe(2d, 1e-4);
    }

    [Fact]
    public void The_voice_a_module_is_set_to_picks_which_of_the_notes_held_at_once_it_plays()
    {
        // A chord of two notes: voice 2 has the second, voice 3 has nothing.
        var path = Write(File(480, Track(On(0, 1, 60), On(0, 1, 64), Off(960, 1, 60), Off(0, 1, 64))));

        double GateOf(int voice)
        {
            var (patch, player) = Playing(path, output: 1);

            player.SetState(MidiLineExtra.Name, new System.Text.Json.Nodes.JsonObject { ["voice"] = voice, ["channel"] = 0 });

            return Heard(patch.CompileForAudio(NodeCatalog.BuiltIn, Library()), 0.5f);
        }

        GateOf(1).ShouldBe(1d);
        GateOf(2).ShouldBe(1d);
        GateOf(3).ShouldBe(0d);
        MidiLineExtra.Voice(Playing(path, output: 1).Player).ShouldBe(0, "a fresh module plays one note at a time");
    }

    [Fact]
    public void A_file_that_is_not_there_is_named_and_the_module_is_silent()
    {
        var (patch, player) = Playing(Path.Combine(folder, "gone.mid"), output: 1);

        var compiled = patch.CompileForAudio(NodeCatalog.BuiltIn, Library());

        var issue = compiled.Issues.ShouldHaveSingleItem();

        issue.NodeId.ShouldBe(player.Id);
        issue.Message.ShouldContain("gone.mid");
        issue.Message.ShouldContain("no file there");
        Heard(compiled, 0.25f).ShouldBe(0d);
    }

    [Fact]
    public void A_module_with_no_file_chosen_says_so()
    {
        var (patch, _) = Playing(string.Empty, output: 1);

        patch.CompileForAudio(NodeCatalog.BuiltIn, Library()).Issues.ShouldHaveSingleItem()
            .Message.ShouldContain("no MIDI file chosen");
    }

    [Fact]
    public void A_channel_the_file_never_uses_is_a_warning()
    {
        var path = Write(File(480, Track(On(0, 1, 60), Off(480, 1, 60))));
        var (patch, player) = Playing(path, output: 1);

        player.SetState(MidiLineExtra.Name, new System.Text.Json.Nodes.JsonObject { ["voice"] = 0, ["channel"] = 5 });

        var issue = patch.CompileForAudio(NodeCatalog.BuiltIn, Library()).Issues.ShouldHaveSingleItem();

        issue.Severity.ShouldBe(IssueSeverity.Warning);
        issue.Message.ShouldContain("channel 5");
    }

    [Fact]
    public void A_file_named_by_a_patch_is_found_beside_it_and_not_read_again_until_forgotten()
    {
        var path = Write(File(480, Track(On(0, 1, 60), Off(480, 1, 60))));
        var library = Library();

        var song = library.FindMidi(Path.GetFileName(path));

        song.ShouldNotBeNull();
        library.FindMidi(Path.GetFileName(path)).ShouldBeSameAs(song);

        library.Forget(Path.GetFileName(path));
        library.FindMidi(Path.GetFileName(path)).ShouldNotBeSameAs(song);
    }

    [Fact]
    public void A_path_that_leaves_the_patchs_folder_is_refused_by_name()
    {
        var library = Library();

        library.FindMidi(@"\\other\share\tune.mid").ShouldBeNull();
        library.ExplainMidi(@"\\other\share\tune.mid").ShouldContain("another machine");
    }

    [Fact]
    public void The_file_is_part_of_what_a_bundle_carries()
    {
        var path = Write(File(480, Track(On(0, 1, 60), Off(480, 1, 60))));
        var (_, player) = Playing(path, output: 1);

        var extra = NodeCatalog.BuiltIn.Require(NodeCatalog.MidiFileTypeId).Extra<MidiFileExtra>().ShouldNotBeNull();

        extra.Files(player).ShouldBe([path]);

        extra.Rebase(player, _ => "tune.mid");
        MidiFileExtra.Of(player).ShouldBe("tune.mid");
    }
}
