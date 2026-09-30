using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.App.Bars;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>The status bar's count of what the patch costs, the picture and the sound each.</summary>
public sealed class StatusCountTests : UiTest
{
    /// <summary>Written on a timer, which a headless run cannot be relied on to tick, so the test writes it.</summary>
    private static bool Until(Func<bool> done, double seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        return done();
    }

    [AvaloniaFact]
    public void It_counts_the_ops_of_the_picture_and_of_the_sound()
    {
        var window = NewMainWindow();
        window.Show();
        Settle(window);

        var bar = Service<StatusBar>(window);
        var count = All<TextBlock>(window).Single(text => text.Name == "statusCount");

        Until(() =>
        {
            bar.Update();
            return count.Text?.Contains(Counted(Editor(window).History.Patch)) == true;
        })
            .ShouldBeTrue($"{count.Text} against {Counted(Editor(window).History.Patch)}");
    }

    private static string Counted(Patch patch) =>
        $"{patch.CompileForVideo(played: true).Program.Ops.Length}/"
        + $"{patch.CompileForAudio(played: true).Program.Ops.Length} picture/sound ops";
}
