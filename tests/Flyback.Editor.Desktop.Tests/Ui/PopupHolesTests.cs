using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Editor.Desktop.Tests.Ui;

/// <summary>
/// The holes a page's picture canvas cuts for the popups over it, found with a tooltip
/// in the window's own layers, where a page puts every popup.
/// </summary>
public class PopupHolesTests : UiTest
{
    [AvaloniaFact]
    public void A_tooltip_over_a_control_is_one_hole_its_size_where_it_lies()
    {
        var (picture, window) = Picture();

        OpenTip(picture);
        Settle(window);

        var tip = All<OverlayPopupHost>(window).Single();
        var holes = PopupHoles.Over(picture);

        holes.Count.ShouldBe(1);
        holes[0].Size.ShouldBe(tip.Bounds.Size);
        holes[0].Center.X.ShouldBe(picture.Bounds.Width / 2, 1);
        holes[0].Center.Y.ShouldBe(picture.Bounds.Height / 2, 1);
    }

    [AvaloniaFact]
    public void A_closed_tooltip_leaves_no_hole()
    {
        var (picture, window) = Picture();

        OpenTip(picture);
        Settle(window);
        ToolTip.SetIsOpen(picture, false);
        Settle(window);

        PopupHoles.Over(picture).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void No_hole_is_no_clip() =>
        PopupHoles.ClipPath(new Size(640, 360), []).ShouldBeNull();

    [AvaloniaFact]
    public void Each_hole_is_a_square_inside_the_whole() =>
        PopupHoles.ClipPath(new Size(640, 360), [new Rect(0, 0, 80, 30.5), new Rect(600, 300, 40, 60)])
            .ShouldBe("M0 0H640V360H0Z M0 0H80V30.5H0Z M600 300H640V360H600Z");

    private (Border Picture, Window Window) Picture()
    {
        var picture = new Border { Width = 480, Height = 270 };

        return (picture, Show(picture, 600));
    }

    private static void OpenTip(Control picture)
    {
        ToolTip.SetTip(picture, "A tip over the picture");
        ToolTip.SetPlacement(picture, PlacementMode.Center);
        ToolTip.SetVerticalOffset(picture, 0);
        ToolTip.SetShouldUseOverlayLayer(picture, true);
        ToolTip.SetIsOpen(picture, true);
    }
}
