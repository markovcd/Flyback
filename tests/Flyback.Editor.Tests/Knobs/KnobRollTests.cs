using Flyback.Editor.Knobs;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Knobs;

/// <summary>Where a randomized knob lands, and how it glides there.</summary>
public class KnobRollTests
{
    [Theory]
    [InlineData(0.5f, 0.1)]
    [InlineData(0.05f, 0.2)]
    [InlineData(0.98f, 0.3)]
    public void A_knob_lands_within_the_amount_of_where_it_was(float at, double amount)
    {
        var random = new Random(7);

        for (var i = 0; i < 1000; i++)
        {
            var next = KnobRoll.Next(at, amount, random);

            next.ShouldBeInRange(Math.Max(0f, at - (float)amount), Math.Min(1f, at + (float)amount));
        }
    }

    [Fact]
    public void A_full_amount_reaches_both_ends_of_the_knob()
    {
        var random = new Random(7);
        var landed = Enumerable.Range(0, 1000).Select(_ => KnobRoll.Next(0.5f, 1, random)).ToList();

        landed.Min().ShouldBeLessThan(0.05f);
        landed.Max().ShouldBeGreaterThan(0.95f);
    }

    [Fact]
    public void No_amount_leaves_the_knob_where_it_is()
    {
        KnobRoll.Next(0.3f, 0, new Random(7)).ShouldBe(0.3f);
    }

    [Fact]
    public void A_glide_starts_where_the_knob_was_and_ends_where_it_is_going()
    {
        var id = Guid.NewGuid();
        var glide = new KnobGlide(new Dictionary<Guid, (float, float)> { [id] = (0.2f, 0.8f) }, TimeSpan.FromSeconds(2));

        glide.At(TimeSpan.Zero).Single().At.ShouldBe(0.2f, 1e-6f);
        glide.At(TimeSpan.FromSeconds(1)).Single().At.ShouldBe(0.5f, 1e-6f);
        glide.At(TimeSpan.FromSeconds(2)).Single().At.ShouldBe(0.8f, 1e-6f);
        glide.Done(TimeSpan.FromSeconds(1)).ShouldBeFalse();
        glide.Done(TimeSpan.FromSeconds(2)).ShouldBeTrue();
    }

    [Fact]
    public void A_glide_of_nought_arrives_at_once()
    {
        var id = Guid.NewGuid();
        var glide = new KnobGlide(new Dictionary<Guid, (float, float)> { [id] = (0.2f, 0.8f) }, TimeSpan.Zero);

        glide.At(TimeSpan.Zero).Single().At.ShouldBe(0.8f);
        glide.Done(TimeSpan.Zero).ShouldBeTrue();
    }

    [Fact]
    public void A_knob_taken_by_hand_leaves_the_glide()
    {
        var taken = Guid.NewGuid();
        var other = Guid.NewGuid();
        var glide = new KnobGlide(
            new Dictionary<Guid, (float, float)> { [taken] = (0f, 1f), [other] = (0f, 1f) },
            TimeSpan.FromSeconds(1));

        glide.Drop(taken);

        glide.At(TimeSpan.FromSeconds(0.5)).Select(move => move.Id).ShouldBe([other]);
    }
}
