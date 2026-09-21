using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// A knob holds the pointer still while it turns, so a knob at the foot of the screen turns
/// all the way on the room there is.
/// </summary>
public class KnobHoldTests : UiTest
{
    /// <summary>Stands in for the platform, whose warp arrives back as a move to where the drag began.</summary>
    private sealed class Anchor(bool holds) : IPointerAnchor
    {
        public int Returns { get; private set; }

        public bool Disposed { get; private set; }

        public bool Return()
        {
            Returns++;

            return holds;
        }

        public void Dispose() => Disposed = true;
    }

    private (Window Window, Knob Knob, Point Middle) Open(IPointerAnchor? anchor)
    {
        var knob = new Knob { Value = 0, Anchor = _ => anchor };
        var window = Show(knob);

        return (window, knob, knob.TranslatePoint(new Point(22, 22), window)!.Value);
    }

    [AvaloniaFact]
    public void A_held_pointer_turns_the_knob_all_the_way_on_a_short_stretch()
    {
        var anchor = new Anchor(holds: true);
        var (window, knob, middle) = Open(anchor);

        window.MouseDown(middle, MouseButton.Left);

        for (var i = 0; i < 4; i++)
        {
            window.MouseMove(middle - new Point(0, 40));
            window.MouseMove(middle);
        }

        window.MouseUp(middle, MouseButton.Left);
        Settle(window);

        knob.Value.ShouldBe(1, 1e-9);
        anchor.Disposed.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_pointer_is_hidden_while_held_and_back_when_let_go()
    {
        var (window, knob, middle) = Open(new Anchor(holds: true));
        var upright = knob.Cursor;

        window.MouseDown(middle, MouseButton.Left);
        window.MouseMove(middle - new Point(0, 10));

        knob.Cursor.ShouldNotBeSameAs(upright);

        window.MouseUp(middle, MouseButton.Left);

        knob.Cursor.ShouldBeSameAs(upright);
    }

    [AvaloniaFact]
    public void A_double_click_still_puts_a_held_knob_back_to_the_middle()
    {
        var (window, knob, middle) = Open(new Anchor(holds: true));

        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);

        knob.Value.ShouldBe(0.5);
    }

    [AvaloniaFact]
    public void Where_the_pointer_cannot_be_held_the_knob_follows_it()
    {
        var (window, knob, middle) = Open(anchor: null);

        window.MouseDown(middle, MouseButton.Left);
        window.MouseMove(middle - new Point(0, 40));
        window.MouseMove(middle);
        window.MouseMove(middle - new Point(0, 40));
        window.MouseUp(middle - new Point(0, 40), MouseButton.Left);

        knob.Value.ShouldBe(0.25, 1e-9);
    }

    [AvaloniaFact]
    public void A_refused_hold_lets_the_pointer_go_for_the_rest_of_the_turn()
    {
        var anchor = new Anchor(holds: false);
        var (window, knob, middle) = Open(anchor);
        var upright = knob.Cursor;

        window.MouseDown(middle, MouseButton.Left);
        window.MouseMove(middle - new Point(0, 40));
        window.MouseMove(middle - new Point(0, 80));

        anchor.Returns.ShouldBe(1);
        anchor.Disposed.ShouldBeTrue();
        knob.Cursor.ShouldBeSameAs(upright);

        window.MouseUp(middle - new Point(0, 80), MouseButton.Left);

        knob.Value.ShouldBe(0.5, 1e-9);
    }
}
