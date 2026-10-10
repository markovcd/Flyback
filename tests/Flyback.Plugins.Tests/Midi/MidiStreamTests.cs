using Flyback.Plugins.Midi;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Midi;

/// <summary>
/// The stream reader Android's input hears through: bytes as the wire carries them, cut
/// into the messages <see cref="MidiMessages.Of"/> reads.
/// </summary>
public class MidiStreamTests
{
    private readonly List<MidiMessage> heard = [];

    private static MidiMessage Down(int note) => new(MidiAction.Down, note, 64 / 127f) { Channel = 1 };

    private static MidiMessage Up(int note) => new(MidiAction.Up, note, 0f) { Channel = 1 };

    private static MidiMessage Tick => new(MidiAction.Tick, 0, 0f);

    private MidiStream Stream() => new(heard.Add);

    [Fact]
    public void A_note_that_arrives_in_two_pieces_is_heard_once_whole()
    {
        var stream = Stream();

        stream.Feed([0x90, 0x3C]);
        heard.ShouldBeEmpty();

        stream.Feed([0x40]);
        heard.ShouldBe([Down(60)]);
    }

    [Fact]
    public void Notes_under_one_status_byte_are_each_heard()
    {
        Stream().Feed([0x90, 0x3C, 0x40, 0x40, 0x40, 0x43, 0x40, 0x3C, 0x00]);

        heard.ShouldBe([Down(60), Down(64), Down(67), Up(60)]);
    }

    [Fact]
    public void A_clock_tick_between_the_bytes_of_a_note_disturbs_neither()
    {
        Stream().Feed([0x90, 0xF8, 0x3C, 0x40]);

        heard.ShouldBe([Tick, Down(60)]);
    }

    [Fact]
    public void An_exclusive_message_is_stepped_over_and_a_tick_inside_it_is_still_heard()
    {
        Stream().Feed([0xF0, 0x7E, 0xF8, 0x00, 0xF7, 0x90, 0x3C, 0x40]);

        heard.ShouldBe([Tick, Down(60)]);
    }

    [Fact]
    public void A_system_common_message_ends_running_status()
    {
        // Song select, then two bytes that belong to nothing.
        Stream().Feed([0x90, 0x3C, 0x40, 0xF3, 0x05, 0x3E, 0x40]);

        heard.ShouldBe([Down(60)]);
    }

    [Fact]
    public void Song_position_carries_its_two_bytes()
    {
        Stream().Feed([0xF2, 0x10, 0x01]);

        heard.ShouldBe([new MidiMessage(MidiAction.Position, 0x10 | (1 << 7), 0f)]);
    }

    [Fact]
    public void A_program_change_takes_one_byte_and_runs_on_like_a_note()
    {
        // Two program changes under one status byte, then a note.
        Stream().Feed([0x90, 0x3C, 0x40, 0xC0, 0x05, 0x06, 0x90, 0x3E, 0x40]);

        heard.ShouldBe([Down(60), Down(62)]);
    }

    [Fact]
    public void Data_bytes_before_any_status_byte_are_dropped()
    {
        Stream().Feed([0x3C, 0x40, 0x90, 0x3C, 0x40]);

        heard.ShouldBe([Down(60)]);
    }

    [Fact]
    public void A_listener_that_throws_loses_one_note_and_hears_the_next()
    {
        var stream = new MidiStream(message =>
        {
            if (message.Note == 60) throw new InvalidOperationException("dropped");

            heard.Add(message);
        });

        stream.Feed([0x90, 0x3C, 0x40, 0x3E, 0x40]);

        heard.ShouldBe([Down(62)]);
    }
}
