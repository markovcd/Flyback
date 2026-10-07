using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests;

/// <summary>What a trace file says: the steps that ran past the threshold, and nothing for the ones that did not.</summary>
public class StallTraceTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"flyback-trace-{Guid.NewGuid():N}.txt");

    private string Written()
    {
        StallTrace.Close();

        return File.ReadAllText(path);
    }

    [Fact]
    public void A_step_over_the_threshold_is_written_down_with_its_name_and_time()
    {
        StallTrace.Open(path);

        using (StallTrace.Step("Slow.Step")) Thread.Sleep(150);

        Written().ShouldContain("Slow.Step");
    }

    [Fact]
    public void A_step_under_the_threshold_says_nothing()
    {
        StallTrace.Open(path);

        using (StallTrace.Step("Quick.Step")) Thread.Sleep(5);

        var text = Written();

        text.ShouldStartWith("# stalls over 100 ms");
        text.ShouldNotContain("Quick.Step");
    }

    [Fact]
    public void A_stall_of_the_UI_thread_says_there_was_no_step_where_none_ran()
    {
        StallTrace.Open(path);

        StallTrace.UiStalled(TimeSpan.FromMilliseconds(300), System.Diagnostics.Stopwatch.GetTimestamp());

        Written().ShouldContain("UI thread (in no named step)");
    }

    [Fact]
    public void No_step_is_timed_while_no_trace_is_open()
    {
        StallTrace.Close();

        StallTrace.On.ShouldBeFalse();
        Should.NotThrow(() =>
        {
            using (StallTrace.Step("Nobody.Hears")) Thread.Sleep(150);
        });
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_refused_where_it_is_opened()
    {
        Should.Throw<DirectoryNotFoundException>(() => StallTrace.Open(Path.Combine(path, "no", "such", "folder.txt")));
        StallTrace.On.ShouldBeFalse();
    }

    public void Dispose()
    {
        StallTrace.Close();

        if (File.Exists(path)) File.Delete(path);

        GC.SuppressFinalize(this);
    }
}
