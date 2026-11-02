using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Ui.Tests.Controls;

/// <summary>The wheel over a knob turns it a step a notch, however finely a trackpad scrolls.</summary>
public class KnobWheelTests
{
    [AvaloniaFact]
    public void A_trackpad_turns_a_knob_once_its_scroll_adds_up_to_a_notch()
    {
        var knob = new Knob { Width = 60, Height = 60 };
        var window = new Window { Width = 100, Height = 100, Content = knob };
        window.Show();

        var at = knob.TranslatePoint(new Point(30, 30), window)!.Value;
        var was = knob.Value;

        window.MouseWheel(at, new Vector(0, 0.4));
        window.MouseWheel(at, new Vector(0, 0.4));
        Dispatcher.UIThread.RunJobs();

        knob.Value.ShouldBe(was, "two fifths of a notch twice is not yet a notch");

        window.MouseWheel(at, new Vector(0, 0.4));
        Dispatcher.UIThread.RunJobs();

        knob.Value.ShouldBe(was + 0.02, 1e-9);

        window.MouseWheel(at, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();

        knob.Value.ShouldBe(was, 1e-9, "a notch back is a step back");
        window.Close();
    }
}
