using Flyback.App.Capture;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Capture;

/// <summary>
/// The picture is drawn when the machine can and written when the file says, and
/// this is the piece that translates. What it must never do is let the two
/// streams disagree about how long the take has been running.
/// </summary>
public class CapturePacerTests
{
    private static CapturePacer At(double rate = 30d) => new(rate);

    [Fact]
    public void Nothing_is_due_before_the_first_frame_time()
    {
        var pacer = At();

        pacer.Due(0d).ShouldBe(0);
        pacer.Due(0.02d).ShouldBe(0);
    }

    /// <summary>
    /// Frame n belongs at n / rate, so the first frame is due the moment the
    /// clock reaches a thirtieth of a second and not a moment sooner.
    /// </summary>
    [Fact]
    public void One_frame_falls_due_each_period()
    {
        var pacer = At();

        pacer.Due(1d / 30d).ShouldBe(1);
        pacer.Commit(1);

        pacer.Due(1d / 30d).ShouldBe(0);

        pacer.Due(2d / 30d).ShouldBe(1);
    }

    /// <summary>
    /// The case the whole class exists for. The renderer stalls for a fifth of a
    /// second, and the file is owed six frames rather than one — which the
    /// recorder pays with repeats so the sound does not run ahead.
    /// </summary>
    [Fact]
    public void A_stall_falls_due_all_at_once()
    {
        var pacer = At();
        pacer.Commit(pacer.Due(1d / 30d));

        pacer.Due(7d / 30d).ShouldBe(6);
    }

    /// <summary>Asking is not taking, or a caller with nothing to write would lose the moment.</summary>
    [Fact]
    public void Asking_twice_answers_the_same_until_it_is_committed()
    {
        var pacer = At();

        pacer.Due(1d).ShouldBe(30);
        pacer.Due(1d).ShouldBe(30);

        pacer.Commit(30);

        pacer.Due(1d).ShouldBe(0);
    }

    /// <summary>
    /// Over a long take the file's length has to stay the clock's length. A
    /// tenth of a second of drift an hour in is a tenth of a second of lip-sync
    /// error, and accumulating a delta per frame is how that happens.
    /// </summary>
    [Fact]
    public void An_hour_of_takes_lands_on_the_hour()
    {
        var pacer = At();

        for (var tick = 1; tick <= 3600 * 100; tick++)
            pacer.Commit(pacer.Due(tick / 100d));

        pacer.Emitted.ShouldBe(3600 * 30);
    }

    /// <summary>Rates that do not divide into whole samples are the normal case, not the exotic one.</summary>
    [Fact]
    public void A_fractional_rate_still_lands_where_it_should()
    {
        var pacer = At(29.97d);

        for (var tick = 1; tick <= 1000; tick++)
            pacer.Commit(pacer.Due(tick / 100d));

        pacer.Emitted.ShouldBe((long)Math.Floor(10d * 29.97d));
    }

    [Fact]
    public void A_clock_that_has_not_started_is_not_a_frame() =>
        At().Due(double.NaN).ShouldBe(0);

    [Fact]
    public void A_rate_of_nothing_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new CapturePacer(0d));
}
