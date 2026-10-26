using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// The card that floats over a module while one of its values turns: there while the
/// value turns and gone after, above the module, below it where there is no room above.
/// </summary>
public class TurnReadoutTests : EditorTest
{
    private static string? ShotDir => Environment.GetEnvironmentVariable("SHOT_DIR");

    private (NodeEditor Editor, Window Window, NodeInstance Sine, int Port) Turning(double y)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = builder.Add(NodeCatalog.SineTypeId, 300, y);
        var def = NodeCatalog.Get(sine.TypeId)!;
        var port = Enumerable.Range(0, def.Inputs.Count).First(i => ValueWell.Has(builder.Patch, sine, def, i));

        var (editor, window) = Editing(builder.Patch, services => services.AddSingleton<IPointerAnchors>(new Free()));
        Settle(window);

        return (editor, window, sine, port);
    }

    private sealed class Free : IPointerAnchors
    {
        public IPointerAnchor? Take(Visual visual) => null;
    }

    private static Point Well(NodeEditor editor, Window window, NodeInstance node, int port)
    {
        var def = NodeCatalog.Get(node.TypeId)!;
        var graph = ValueWell.At(Geometry.Bounds(node, def), Geometry.InputPort(node, def, port)).Center;

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)!.Value;
    }

    [AvaloniaFact]
    public void A_turn_shows_the_readout_and_letting_go_takes_it_away()
    {
        var (editor, window, sine, port) = Turning(200);
        var at = Well(editor, window, sine, port);

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at - new Point(0, 30));
        Settle(window);

        var (pick, turn) = editor.Dial.Reading.ShouldNotBeNull();
        pick.Port.ShouldBe(port);
        turn.Value.ShouldBe(sine.InputValues[port]);
        turn.Was.ShouldNotBe(turn.Value);

        if (ShotDir is { } dir) Save(window, Path.Combine(dir, "turn-readout.png"));

        window.MouseUp(at - new Point(0, 30), MouseButton.Left);
        Settle(window);

        editor.Dial.Reading.ShouldBeNull();
    }

    [AvaloniaFact]
    public void The_readout_stands_above_the_module_and_below_it_where_there_is_no_room()
    {
        var view = new Size(1200, 800);
        var well = new Rect(500, 330, 56, 18);

        var (high, above) = TurnReadout.Place(new Rect(400, 300, 196, 140), well, view);
        above.ShouldBeTrue();
        high.Bottom.ShouldBeLessThanOrEqualTo(300);

        var (low, onTop) = TurnReadout.Place(new Rect(400, 20, 196, 140), well, view);
        onTop.ShouldBeFalse("there is no room for it above a module at the top of the view");
        low.Top.ShouldBeGreaterThanOrEqualTo(160);
    }

    [AvaloniaFact]
    public void The_readout_stays_inside_the_view()
    {
        var view = new Size(600, 800);
        var (card, _) = TurnReadout.Place(new Rect(-100, 400, 196, 140), new Rect(-20, 430, 56, 18), view);

        card.Left.ShouldBeGreaterThanOrEqualTo(0);
        card.Right.ShouldBeLessThanOrEqualTo(view.Width);
    }

    [AvaloniaFact]
    public void The_readout_says_where_the_turn_began_and_by_how_much_it_moved()
    {
        var number = new PortSpec("freq", PortKind.Scalar);

        TurnReadout.Change(new TurnReadout.Turn("freq", "Sine", number, 1.46f, 0.8f, -4, 4, WellState.Turn)).ShouldBe("was 0.8 · +0.66");
        TurnReadout.Change(new TurnReadout.Turn("freq", "Sine", number, 0.5f, 0.8f, -4, 4, WellState.Turn)).ShouldBe("was 0.8 · −0.3");
    }

    private static void Save(Window window, string path)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
        using var file = File.Create(path);
        frame.Save(file, new PngBitmapEncoderOptions());
    }
}
