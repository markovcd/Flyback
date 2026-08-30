using Avalonia.Input;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Midi;

/// <summary>
/// The half of a MIDI In that lives in the shell: two rows of a typewriter read
/// as a piano, with simultaneous keys assigned to indexed voices.
/// </summary>
/// <remarks>
/// What is worth pinning here is voice allocation and release. Everything else
/// about the module is arithmetic the engine's own tests cover; this is where a
/// key going down and coming up in the wrong order can leave a note sounding for
/// the rest of the session.
/// </remarks>
public class KeyboardTests
{
    private static readonly string Pitch = MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Pitch);
    private static readonly string Gate = MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Gate);
    private static readonly string Velocity = MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Velocity);
    private static readonly string Strikes = MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Strikes);
    private static readonly string Pitch2 = MidiSignal.Key(MidiSources.Keyboard, 2, MidiSignal.Pitch);
    private static readonly string Gate2 = MidiSignal.Key(MidiSources.Keyboard, 2, MidiSignal.Gate);
    private static readonly string Strikes2 = MidiSignal.Key(MidiSources.Keyboard, 2, MidiSignal.Strikes);

    /// <summary>A block reading everything one keyboard carries.</summary>
    private static LiveValues Block() => new([Pitch, Gate, Velocity, Strikes, Pitch2, Gate2, Strikes2]);

    private static double Read(LiveValues block, string key) =>
        block.At(block.Keys.ToList().IndexOf(key));

    [Fact]
    public void The_bottom_row_is_the_white_notes_of_an_octave()
    {
        var keys = new ComputerKeyboard();

        // C3 upwards: Z X C V B N M is C D E F G A B, and the row above holds
        // the black notes over the gaps.
        keys.Note(Key.Z).ShouldBe(48);
        keys.Note(Key.S).ShouldBe(49);
        keys.Note(Key.X).ShouldBe(50);
        keys.Note(Key.M).ShouldBe(59);

        // And the row above is the octave up, so Q is the C over Z.
        keys.Note(Key.Q).ShouldBe(60);
    }

    /// <summary>Every other key goes on meaning what it meant to the editor.</summary>
    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.Delete)]
    [InlineData(Key.F)]
    [InlineData(Key.Escape)]
    public void A_key_that_is_not_a_note_plays_none(Key key)
    {
        new ComputerKeyboard().Note(key).ShouldBeNull();
    }

    [Fact]
    public void The_octave_moves_both_rows_together()
    {
        var keys = new ComputerKeyboard { Octave = 1 };

        keys.Note(Key.Z).ShouldBe(60);
        keys.Note(Key.Q).ShouldBe(72);
    }

    /// <summary>
    /// Held to what leaves every key on the layout a note that exists, so no
    /// amount of pressing Page Up asks for a note past the end of the scale.
    /// </summary>
    [Fact]
    public void The_octave_stops_where_the_notes_do()
    {
        var keys = new ComputerKeyboard { Octave = 99 };
        keys.Note(Key.P)!.Value.ShouldBeInRange(0, 127);

        keys.Octave = -99;
        keys.Note(Key.Z)!.Value.ShouldBeInRange(0, 127);
    }

    [Fact]
    public void A_key_down_plays_its_note()
    {
        var hub = new MidiHub();
        var block = Block();
        hub.Follow(block);

        hub.KeyDown(Key.Z).ShouldBeTrue();

        Read(block, Pitch).ShouldBe(48d);
        Read(block, Gate).ShouldBe(1d);
        Read(block, Velocity).ShouldBe(ComputerKeyboard.Velocity, 0.0001);
        Read(block, Strikes).ShouldBe(1d);
    }

    [Fact]
    public void A_key_that_is_not_a_note_is_left_for_whatever_else_wanted_it()
    {
        var hub = new MidiHub();

        hub.KeyDown(Key.Space).ShouldBeFalse();
        hub.KeyUp(Key.Space).ShouldBeFalse();
    }

    [Fact]
    public void Letting_the_last_key_go_shuts_the_gate()
    {
        var hub = new MidiHub();
        var block = Block();
        hub.Follow(block);

        hub.KeyDown(Key.Z);
        hub.KeyUp(Key.Z);

        Read(block, Gate).ShouldBe(0d);

        // The pitch stays where it was: a release is still sounding while the
        // envelope runs, and it should decay at the note it was played at.
        Read(block, Pitch).ShouldBe(48d);
    }

    /// <summary>Two computer keys occupy two indexed voices independently.</summary>
    [Fact]
    public void A_second_key_takes_the_voice_over()
    {
        var hub = new MidiHub();
        var block = Block();
        hub.Follow(block);

        hub.KeyDown(Key.Z);
        hub.KeyDown(Key.X);

        Read(block, Pitch).ShouldBe(48d);
        Read(block, Strikes).ShouldBe(1d);
        Read(block, Pitch2).ShouldBe(50d);
        Read(block, Gate2).ShouldBe(1d);
        Read(block, Strikes2).ShouldBe(1d);
    }

    /// <summary>Releasing one computer key does not release the other voice.</summary>
    [Fact]
    public void Letting_the_newer_key_go_falls_back_to_the_one_still_held()
    {
        var hub = new MidiHub();
        var block = Block();
        hub.Follow(block);

        hub.KeyDown(Key.Z);
        hub.KeyDown(Key.X);
        hub.KeyUp(Key.X);

        Read(block, Pitch).ShouldBe(48d);
        Read(block, Gate).ShouldBe(1d);

        Read(block, Gate2).ShouldBe(0d);
    }

    /// <summary>
    /// Auto-repeat is a key already down being pressed again. Held twice it would
    /// need letting go twice, and the second half of that never comes.
    /// </summary>
    [Fact]
    public void Holding_a_key_down_does_not_make_it_two_notes()
    {
        var hub = new MidiHub();
        var block = Block();
        hub.Follow(block);

        hub.KeyDown(Key.Z);
        hub.KeyDown(Key.X);
        hub.KeyDown(Key.X);
        hub.KeyDown(Key.X);
        hub.KeyUp(Key.X);

        Read(block, Pitch).ShouldBe(48d);
        Read(block, Gate).ShouldBe(1d);
    }

    /// <summary>
    /// The window losing the focus mid-chord. Without this the release lands
    /// somewhere else and the note is held until something happens to move it.
    /// </summary>
    [Fact]
    public void Everything_is_let_go_when_the_window_stops_listening()
    {
        var hub = new MidiHub();
        var block = Block();
        hub.Follow(block);

        hub.KeyDown(Key.Z);
        hub.KeyDown(Key.X);
        hub.AllOff();

        Read(block, Gate).ShouldBe(0d);
    }

    /// <summary>
    /// Shifting the octave lets go first, because a note released after the shift
    /// would be a different note from the one pressed and would never be found.
    /// </summary>
    [Fact]
    public void Moving_the_octave_lets_go_of_what_is_held()
    {
        var hub = new MidiHub();
        var block = Block();
        hub.Follow(block);

        hub.KeyDown(Key.Z);
        hub.Shift(Key.PageUp).ShouldNotBeNull();

        Read(block, Gate).ShouldBe(0d);
        hub.Keyboard.Octave.ShouldBe(1);

        hub.KeyDown(Key.Z);
        Read(block, Pitch).ShouldBe(60d);
    }

    [Fact]
    public void A_key_that_does_not_move_the_octave_says_so()
    {
        new MidiHub().Shift(Key.Z).ShouldBeNull();
    }

    /// <summary>
    /// A recompile makes new blocks, and a note held across one must still be
    /// held after it — otherwise every knob turned while playing would cut the
    /// note off.
    /// </summary>
    [Fact]
    public void A_note_held_across_a_recompile_is_still_held()
    {
        var hub = new MidiHub();
        hub.Follow(Block());

        hub.KeyDown(Key.Z);

        var recompiled = Block();
        hub.Follow(recompiled);

        Read(recompiled, Pitch).ShouldBe(48d);
        Read(recompiled, Gate).ShouldBe(1d);
    }

    /// <summary>
    /// A block that reads nothing is what every patch without a MIDI In has, and
    /// playing into one is meant to be silently harmless rather than an error.
    /// </summary>
    [Fact]
    public void Playing_into_a_patch_that_is_not_listening_does_nothing()
    {
        var hub = new MidiHub();
        hub.Follow(LiveValues.None);

        Should.NotThrow(() => hub.KeyDown(Key.Z));
    }

    /// <summary>The picture is redrawn for a key even when the clock is stopped.</summary>
    [Fact]
    public void Something_played_is_announced()
    {
        var hub = new MidiHub();
        var told = 0;

        hub.Played += () => told++;
        hub.Follow(Block());
        hub.KeyDown(Key.Z);

        told.ShouldBeGreaterThan(0);
    }
}
