using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

/// <summary>
/// A pinch over the text resizes its font in whole points, by two fingers on a screen
/// or on a trackpad, and the size is the one Ctrl+scroll and the buttons set.
/// </summary>
public class SourceViewPinchTests : SourceViewTest
{
    private static (SourceView Source, TextEditor Text) Opened(MainWindow window)
    {
        var text = ShowCode(window);
        var source = All<SourceView>(window).Single();

        source.EditorFontSize = CanvasSettings.DefaultEditorFontSize;
        return (source, text);
    }

    /// <summary>
    /// Two fingers on the text, <paramref name="from"/> pixels apart either side of its middle,
    /// spread or closed in ten steps to <paramref name="to"/> apart, then lifted.
    /// Raised on the text area, since headless Avalonia has no touch input.
    /// </summary>
    private static void Pinch(TextEditor text, MainWindow window, double from, double to)
    {
        var area = text.TextArea;
        var middle = MiddleOf(area, window);
        var one = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
        var two = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, false);
        var down = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var moving = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
        var up = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        ulong time = 1_000;

        Point Left(double apart) => middle - new Vector(apart / 2, 0);
        Point Right(double apart) => middle + new Vector(apart / 2, 0);

        area.RaiseEvent(new PointerPressedEventArgs(area, one, window, Left(from), time, down, KeyModifiers.None));
        area.RaiseEvent(new PointerPressedEventArgs(area, two, window, Right(from), time += 16, down, KeyModifiers.None));

        for (var step = 1; step <= 10; step++)
        {
            var apart = from + (to - from) * step / 10;
            area.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, area, one, window, Left(apart), time += 16, moving, KeyModifiers.None));
            area.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, area, two, window, Right(apart), time += 16, moving, KeyModifiers.None));
        }

        area.RaiseEvent(new PointerReleasedEventArgs(area, two, window, Right(to), time += 16, up, KeyModifiers.None, MouseButton.Left));
        area.RaiseEvent(new PointerReleasedEventArgs(area, one, window, Left(to), time + 16, up, KeyModifiers.None, MouseButton.Left));
        Settle(window);
    }

    private static void Magnify(TextEditor text, MainWindow window, double magnification)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);

        text.RaiseEvent(new PointerDeltaEventArgs(
            InputElement.PointerTouchPadGestureMagnifyEvent, text, pointer, window, new Point(50, 50), 1_000, properties, KeyModifiers.None, new Vector(magnification, magnification)));
    }

    [AvaloniaFact]
    public void Two_fingers_spreading_on_the_text_scale_its_size_by_how_far_they_spread()
    {
        var window = Open();
        var (source, text) = Opened(window);

        Pinch(text, window, 40, 80);
        source.EditorFontSize.ShouldBe(2 * CanvasSettings.DefaultEditorFontSize);

        // The next pinch starts from the size the last one left.
        Pinch(text, window, 80, 60);
        source.EditorFontSize.ShouldBe(Math.Round(0.75 * 2 * CanvasSettings.DefaultEditorFontSize));
    }

    [AvaloniaFact]
    public void A_pinch_holds_to_the_settings_bounds_and_whole_points()
    {
        var window = Open();
        var (source, text) = Opened(window);

        Pinch(text, window, 20, 400);
        source.EditorFontSize.ShouldBe(CanvasSettings.MaxEditorFontSize);

        Pinch(text, window, 400, 4);
        source.EditorFontSize.ShouldBe(CanvasSettings.MinEditorFontSize);

        Pinch(text, window, 100, 104);
        source.EditorFontSize.ShouldBe(Math.Round(1.04 * CanvasSettings.MinEditorFontSize));
    }

    [AvaloniaFact]
    public void One_finger_leaves_the_size_alone()
    {
        var window = Open();
        var (source, text) = Opened(window);
        var area = text.TextArea;

        FingerAlong(area, window, MiddleOf(area, window), Steps(MiddleOf(area, window), new Vector(0, -60)));

        source.EditorFontSize.ShouldBe(CanvasSettings.DefaultEditorFontSize);
    }

    [AvaloniaFact]
    public void Small_trackpad_steps_add_up_to_whole_points()
    {
        var window = Open();
        var (source, text) = Opened(window);
        var start = source.EditorFontSize;

        // Each step alone is under half a point.
        for (var step = 0; step < 10; step++) Magnify(text, window, 0.02);

        source.EditorFontSize.ShouldBe(Math.Round(start * Math.Pow(1.02, 10)));

        for (var step = 0; step < 10; step++) Magnify(text, window, -0.02);

        source.EditorFontSize.ShouldBe(Math.Round(start * Math.Pow(1.02, 10) * Math.Pow(0.98, 10)));
    }

    [AvaloniaFact]
    public void A_trackpad_pinch_after_Ctrl_and_scroll_starts_from_the_size_it_set()
    {
        var window = Open();
        var (source, text) = Opened(window);

        Magnify(text, window, 0.2);
        source.EditorFontSize = 30;

        Magnify(text, window, 0.1);
        source.EditorFontSize.ShouldBe(33);
    }

    [AvaloniaFact]
    public void A_pinch_saves_the_size_for_next_time()
    {
        var window = Open();
        var (_, text) = Opened(window);

        Pinch(text, window, 40, 80);

        Service<CanvasSection>(window).EditorFontSize.ShouldBe(2 * CanvasSettings.DefaultEditorFontSize);
    }
}
