using Flyback.App.Capture;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Capture;

/// <summary>
/// The ring is the only thing a recording puts in front of the sound callback,
/// so what matters is that it never blocks, never allocates, and never hands
/// back samples in the wrong order — including across the wrap, which is the
/// only interesting moment it has.
/// </summary>
public class AudioRingTests
{
    private static float[] Ramp(int count, int from = 0)
    {
        var samples = new float[count];

        for (var i = 0; i < count; i++) samples[i] = from + i;

        return samples;
    }

    private static float[] Drain(AudioRing ring, int most)
    {
        var buffer = new float[most];
        var taken = ring.Read(buffer);

        return buffer[..taken];
    }

    [Fact]
    public void What_goes_in_comes_out()
    {
        var ring = new AudioRing(64);
        ring.Write(Ramp(10));

        Drain(ring, 64).ShouldBe(Ramp(10));
    }

    [Fact]
    public void An_empty_ring_gives_nothing() =>
        Drain(new AudioRing(64), 64).ShouldBeEmpty();

    /// <summary>Several callbacks' worth come back as one run of samples in order.</summary>
    [Fact]
    public void Writes_join_up()
    {
        var ring = new AudioRing(64);

        ring.Write(Ramp(10));
        ring.Write(Ramp(10, 10));

        Drain(ring, 64).ShouldBe(Ramp(20));
    }

    /// <summary>
    /// The consumer takes what fits and the rest stays put — a drain smaller than
    /// what is waiting is the ordinary case, not an error.
    /// </summary>
    [Fact]
    public void A_partial_drain_leaves_the_rest_in_order()
    {
        var ring = new AudioRing(64);
        ring.Write(Ramp(20));

        Drain(ring, 8).ShouldBe(Ramp(8));
        Drain(ring, 64).ShouldBe(Ramp(12, 8));
    }

    /// <summary>
    /// The one place the arithmetic can go wrong: a write that starts near the end
    /// of the array and finishes at the start of it.
    /// </summary>
    [Fact]
    public void Samples_survive_the_wrap()
    {
        var ring = new AudioRing(16);

        ring.Write(Ramp(12));
        Drain(ring, 12).ShouldBe(Ramp(12));

        // Starts at 12, so eight samples run off the end and back round.
        ring.Write(Ramp(10, 100));

        Drain(ring, 16).ShouldBe(Ramp(10, 100));
    }

    /// <summary>And a read that has to wrap as well, which is the other half of it.</summary>
    [Fact]
    public void Reads_survive_the_wrap()
    {
        var ring = new AudioRing(16);

        ring.Write(Ramp(12));
        Drain(ring, 12);

        ring.Write(Ramp(12, 200));

        Drain(ring, 5).ShouldBe(Ramp(5, 200));
        Drain(ring, 16).ShouldBe(Ramp(7, 205));
    }

    [Fact]
    public void It_fills_exactly_to_the_brim()
    {
        var ring = new AudioRing(16);
        ring.Write(Ramp(16));

        ring.Dropped.ShouldBe(0);
        Drain(ring, 16).ShouldBe(Ramp(16));
    }

    /// <summary>
    /// An overrun loses the whole buffer rather than part of one. Half a buffer
    /// would shift every later sample by a channel and swap the stereo image for
    /// the rest of the take, which is far worse than the gap.
    /// </summary>
    [Fact]
    public void An_overrun_drops_whole_buffers_and_counts_them()
    {
        var ring = new AudioRing(16);

        ring.Write(Ramp(10));
        ring.Write(Ramp(10, 100));

        ring.Dropped.ShouldBe(10);
        Drain(ring, 16).ShouldBe(Ramp(10));
    }

    /// <summary>Room made by draining is room the next callback can use.</summary>
    [Fact]
    public void Draining_makes_room_again()
    {
        var ring = new AudioRing(16);

        ring.Write(Ramp(16));
        Drain(ring, 16);

        ring.Write(Ramp(16, 50));

        ring.Dropped.ShouldBe(0);
        Drain(ring, 16).ShouldBe(Ramp(16, 50));
    }

    /// <summary>
    /// The real arrangement: one thread writing callback-sized buffers while
    /// another drains. Nothing may be reordered, duplicated or invented.
    /// </summary>
    [Fact]
    public void A_producer_and_a_consumer_agree_on_every_sample()
    {
        const int callback = 128;
        const int total = callback * 1600;

        var ring = new AudioRing(4096);
        var seen = new List<float>(total);

        var producer = new Thread(() =>
        {
            for (var at = 0; at < total; at += callback)
            {
                // Retry rather than lose it, so the assertion below can be exact.
                // A real callback drops instead, which is what Dropped counts.
                var before = ring.Dropped;
                ring.Write(Ramp(callback, at));

                while (ring.Dropped != before)
                {
                    Thread.Yield();

                    before = ring.Dropped;
                    ring.Write(Ramp(callback, at));
                }
            }
        });

        producer.Start();

        var buffer = new float[512];

        while (seen.Count < total)
        {
            var taken = ring.Read(buffer);

            if (taken == 0) Thread.Yield();
            else seen.AddRange(buffer[..taken]);
        }

        producer.Join();

        seen.Count.ShouldBe(total);

        for (var i = 0; i < total; i++)
            if (seen[i] != i)
                throw new Exception($"sample {i} came back as {seen[i]}");
    }
}
