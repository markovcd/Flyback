using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests;

public sealed class SlowStartTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("slow-start").FullName;

    private string File => Path.Combine(folder, "last-start.txt");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void A_quick_launch_after_a_quick_one_has_no_splash() =>
        SlowStart.Splashes(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(1.2)).ShouldBeFalse();

    [Fact]
    public void A_launch_slow_to_get_this_far_has_a_splash() =>
        SlowStart.Splashes(SlowStart.ReadyLate, TimeSpan.FromSeconds(1.2)).ShouldBeTrue();

    [Fact]
    public void A_launch_after_one_slow_to_show_its_window_has_a_splash() =>
        SlowStart.Splashes(TimeSpan.FromSeconds(0.4), SlowStart.WindowLate).ShouldBeTrue();

    [Fact]
    public void A_first_launch_goes_by_this_launch_alone() =>
        SlowStart.Splashes(TimeSpan.FromSeconds(0.4), null).ShouldBeFalse();

    [Fact]
    public void The_time_kept_is_read_back_by_the_next_launch()
    {
        SlowStart.Remember(File, TimeSpan.FromMilliseconds(1830));

        SlowStart.Last(File).ShouldBe(TimeSpan.FromMilliseconds(1830));
    }

    [Fact]
    public void No_file_is_no_time() => SlowStart.Last(File).ShouldBeNull();

    [Fact]
    public void A_damaged_file_is_no_time()
    {
        System.IO.File.WriteAllText(File, "soon");

        SlowStart.Last(File).ShouldBeNull();
    }

    [AvaloniaFact]
    public void The_splash_names_the_program()
    {
        var splash = new SplashWindow();

        splash.Show();

        splash.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ShouldContain("Flyback");
        splash.Close();
    }
}
