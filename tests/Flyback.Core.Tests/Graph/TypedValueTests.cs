using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A value typed for a socket reads back in every form the socket writes it in, and as
/// a plain number; anything else reads as nothing.
/// </summary>
public class TypedValueTests
{
    private static PortSpec Shown(PortDisplay display) => new("x", PortKind.Scalar) { Display = display };

    [Theory]
    [InlineData("0.9", 0.9f)]
    [InlineData("-2", -2f)]
    [InlineData("−2", -2f)]
    [InlineData(" 1e2 ", 100f)]
    public void A_number_reads_as_itself(string typed, float value) =>
        Shown(PortDisplay.Number).Read(typed).ShouldBe(value);

    [Theory]
    [InlineData("")]
    [InlineData("0..8x")]
    [InlineData("A3")]
    [InlineData("NaN")]
    public void Anything_else_reads_as_nothing(string typed) =>
        Shown(PortDisplay.Number).Read(typed).ShouldBeNull();

    [Theory]
    [InlineData("A3", 57f)]
    [InlineData("a4", 69f)]
    [InlineData("C#4", 61f)]
    [InlineData("Bb2", 46f)]
    [InlineData("C-1", 0f)]
    [InlineData("60", 60f)]
    public void A_note_reads_by_its_name_or_its_number(string typed, float value) =>
        Shown(PortDisplay.Note).Read(typed).ShouldBe(value);

    [Fact]
    public void Every_note_name_reads_back_as_its_note()
    {
        var note = Shown(PortDisplay.Note);

        for (var n = 0; n < 128; n++) note.Read(note.Format(n)).ShouldBe(n);
    }

    [Theory]
    [InlineData("1 s", 0f)]
    [InlineData("100 ms", -1f)]
    [InlineData("10ms", -2f)]
    [InlineData("100 µs", -4f)]
    [InlineData("100 us", -4f)]
    [InlineData("10", 1f)]
    public void A_duration_reads_with_its_unit_and_a_bare_number_as_seconds(string typed, float decades) =>
        Shown(PortDisplay.Duration).Read(typed).ShouldNotBeNull().ShouldBe(decades, 1e-5f);

    [Theory]
    [InlineData("0 s")]
    [InlineData("-1 ms")]
    [InlineData("ms")]
    public void A_duration_of_nothing_or_less_reads_as_nothing(string typed) =>
        Shown(PortDisplay.Duration).Read(typed).ShouldBeNull();

    [Fact]
    public void A_chord_reads_by_its_name_or_its_place_in_the_list()
    {
        var chord = Shown(PortDisplay.Chord);

        chord.Read("maj").ShouldBe(4f);
        chord.Read("MIN7").ShouldBe(12f);
        chord.Read("4").ShouldBe(4f);
        chord.Read("major").ShouldBeNull();
    }

    [Fact]
    public void A_whole_number_rounds_what_is_typed() =>
        Shown(PortDisplay.Integer).Read("2.6").ShouldBe(3f);
}
