using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// <see cref="LiveValues"/> — the block of numbers a program is being played
/// with, written by whoever is holding the keys down and read by the ops that
/// name them.
/// </summary>
/// <remarks>
/// Nearly everything here is about the two ends disagreeing, because they are
/// meant to: the block and the program are swapped separately, and a keyboard
/// writes into it without knowing which patch is loaded. A block of the wrong
/// size, a key no module reads and a value that is not a number are all ordinary
/// traffic rather than faults — and none of them may throw, because the thread
/// they would throw on is the one feeding the speakers.
/// </remarks>
public class LiveValuesTests
{
    private static LiveValues Keyboard() => new(["keyboard/gate", "keyboard/pitch"]);

    [Fact]
    public void It_hands_back_what_was_played()
    {
        var live = Keyboard();

        live.Set("keyboard/pitch", 0.25f);

        live.At(1).ShouldBe(0.25d);
    }

    /// <summary>
    /// The case the bounds check exists for: a callback holding the previous
    /// program for one more buffer is reading the new program's block, and an
    /// index past the end has to be silence rather than a fault.
    /// </summary>
    [Fact]
    public void An_input_the_block_does_not_have_reads_as_silence()
    {
        var live = Keyboard();

        live.At(2).ShouldBe(0d);
        live.At(-1).ShouldBe(0d);
    }

    /// <summary>
    /// Ignoring an unread key is the point rather than a shortcut: what writes
    /// here is playing a keyboard, and it should not have to know whether the
    /// module reading it was deleted a moment ago.
    /// </summary>
    [Fact]
    public void Playing_a_key_no_module_reads_changes_nothing()
    {
        var live = Keyboard();

        Should.NotThrow(() => live.Set("nonesuch/gate", 1f));

        live.At(0).ShouldBe(0d);
        live.At(1).ShouldBe(0d);
    }

    [Fact]
    public void It_says_which_keys_it_reads()
    {
        var live = Keyboard();

        live.Reads("keyboard/gate").ShouldBeTrue();
        live.Reads("nonesuch/gate").ShouldBeFalse();
        live.Keys.ShouldBe(new[] { "keyboard/gate", "keyboard/pitch" });
        live.Count.ShouldBe(2);
    }

    /// <summary>
    /// A register is a double and every op reads one unchecked, so an infinity
    /// admitted here would reach the arithmetic rather than be trapped by it.
    /// </summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void A_value_that_is_not_a_number_is_played_as_zero(float played)
    {
        var live = Keyboard();

        live.Set("keyboard/gate", 1f);
        live.Set("keyboard/gate", played);

        live.At(0).ShouldBe(0d);
    }

    [Fact]
    public void Clearing_it_puts_every_input_back_to_nothing()
    {
        var live = Keyboard();

        live.Set("keyboard/gate", 1f);
        live.Set("keyboard/pitch", -3f);

        live.Clear();

        live.At(0).ShouldBe(0d);
        live.At(1).ShouldBe(0d);
    }

    /// <summary>
    /// The shader is handed these as uniforms sized by the program it was built
    /// from, so a destination longer than the block has to be silent the rest of
    /// the way rather than hold whatever the last frame left there.
    /// </summary>
    [Fact]
    public void Copying_out_fills_the_rest_of_a_longer_destination_with_silence()
    {
        var live = Keyboard();
        live.Set("keyboard/gate", 1f);

        Span<float> uniforms = stackalloc float[4];
        uniforms.Fill(9f);

        live.CopyTo(uniforms);

        uniforms[0].ShouldBe(1f);
        uniforms[1].ShouldBe(0f);
        uniforms[2].ShouldBe(0f);
        uniforms[3].ShouldBe(0f);
    }

    /// <summary>A destination shorter than the block takes what fits of it and does not overrun.</summary>
    [Fact]
    public void Copying_out_truncates_to_a_shorter_destination()
    {
        var live = Keyboard();
        live.Set("keyboard/gate", 1f);
        live.Set("keyboard/pitch", 2f);

        Span<float> uniforms = stackalloc float[1];

        live.CopyTo(uniforms);

        uniforms[0].ShouldBe(1f);
    }

    /// <summary>
    /// What a program with no live inputs is given and what a caller that is not
    /// playing anything passes, so it has to answer both without being special
    /// cased by either.
    /// </summary>
    [Fact]
    public void The_shared_empty_block_reads_nothing_and_takes_any_write()
    {
        LiveValues.None.Count.ShouldBe(0);
        LiveValues.None.Keys.ShouldBeEmpty();
        LiveValues.None.At(0).ShouldBe(0d);
        LiveValues.None.Reads("keyboard/gate").ShouldBeFalse();

        Should.NotThrow(() => LiveValues.None.Set("keyboard/gate", 1f));
    }
}
