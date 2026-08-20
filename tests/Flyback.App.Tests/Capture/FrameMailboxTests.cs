using Flyback.App.Capture;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Capture;

/// <summary>
/// One frame deep and newest wins. The behaviour worth pinning is the discarding:
/// the preview draws far more frames than the file wants, and the ones in between
/// have to go somewhere cheaper than an encoder.
/// </summary>
public class FrameMailboxTests
{
    private static void Put(FrameMailbox mailbox, byte value)
    {
        mailbox.Writing.Fill(value);
        mailbox.Publish();
    }

    [Fact]
    public void Nothing_has_been_published_to_begin_with()
    {
        var mailbox = new FrameMailbox(4);

        mailbox.TakeLatest().IsEmpty.ShouldBeTrue();
        mailbox.Published.ShouldBe(0);
    }

    [Fact]
    public void A_published_frame_comes_back()
    {
        var mailbox = new FrameMailbox(4);
        Put(mailbox, 7);

        mailbox.TakeLatest().ToArray().ShouldBe([7, 7, 7, 7]);
    }

    /// <summary>Taking the same frame twice would write it to the file twice for no reason.</summary>
    [Fact]
    public void The_same_frame_is_not_offered_again()
    {
        var mailbox = new FrameMailbox(4);
        Put(mailbox, 7);

        mailbox.TakeLatest().IsEmpty.ShouldBeFalse();
        mailbox.TakeLatest().IsEmpty.ShouldBeTrue();
    }

    /// <summary>The point of the class: surplus frames are dropped, not queued.</summary>
    [Fact]
    public void Only_the_newest_survives()
    {
        var mailbox = new FrameMailbox(4);

        Put(mailbox, 1);
        Put(mailbox, 2);
        Put(mailbox, 3);

        mailbox.TakeLatest().ToArray().ShouldBe([3, 3, 3, 3]);
        mailbox.TakeLatest().IsEmpty.ShouldBeTrue();
    }

    /// <summary>
    /// The producer must get a buffer back that nobody is reading, or the frame
    /// being encoded would be overwritten underneath the encoder.
    /// </summary>
    [Fact]
    public void The_frame_in_hand_is_not_the_one_being_filled()
    {
        var mailbox = new FrameMailbox(4);

        Put(mailbox, 1);
        var taken = mailbox.TakeLatest();

        Put(mailbox, 2);
        Put(mailbox, 3);

        taken.ToArray().ShouldBe([1, 1, 1, 1]);
    }

    /// <summary>
    /// The real arrangement: a render thread publishing flat out while a slower
    /// consumer takes what it can. Every frame collected must be one that was
    /// actually published, never a half-written mixture of two.
    /// </summary>
    [Fact]
    public void A_frame_is_never_collected_half_written()
    {
        const int published = 20_000;

        var mailbox = new FrameMailbox(512);
        var torn = 0;
        var collected = 0;

        var producer = new Thread(() =>
        {
            for (var i = 1; i <= published; i++)
            {
                mailbox.Writing.Fill((byte)(i % 251));
                mailbox.Publish();
            }
        });

        producer.Start();

        while (producer.IsAlive || mailbox.Published > 0 && collected == 0)
        {
            var frame = mailbox.TakeLatest();
            if (frame.IsEmpty) continue;

            collected++;

            var first = frame[0];
            foreach (var b in frame)
                if (b != first)
                {
                    torn++;
                    break;
                }
        }

        producer.Join();

        collected.ShouldBeGreaterThan(0);
        torn.ShouldBe(0);
    }
}
