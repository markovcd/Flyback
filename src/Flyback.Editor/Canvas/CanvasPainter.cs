using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The order the canvas is drawn in: the ground and its grid, the wires, the modules,
/// the boxes and open groups, and what the gestures under way add over them.
/// </summary>
/// <remarks>
/// Drawn inside the view's one matrix, and every socket placed by the same
/// <see cref="NodeGeometry"/> the hit testing measures with, which is what stops a
/// wire's end and the dot it is drawn on from ever disagreeing (ADR-0017).
/// </remarks>
internal sealed class CanvasPainter(
    CanvasHistory history,
    CanvasSelection selection,
    Viewport view,
    CanvasGestures gestures,
    SocketDial dial,
    SocketFocus focus,
    KnobChoice choice,
    StepBar bar,
    RemapMarks marks,
    MeasureLabels measured,
    LevelShelves shelves,
    NodeGeometry geometry,
    CanvasLift lift,
    CanvasGlass glass,
    GroundPainter ground,
    WirePainter wires,
    ModulePainter modules,
    GroupPainter groups)
{
    private static readonly IPen TetherPen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Attention), 2);

    private static readonly IPen AnchorPen = new ImmutablePen(
        new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)), 2, new ImmutableDashStyle([3, 2], 0));

    private Patch Patch => history.Patch;

    public void Render(DrawingContext context)
    {
        if (!glass.SeeThrough) ground.DrawGround(context);
        // Clear, but there: an empty patch of picture still pans and draws a marquee.
        else context.FillRectangle(Brushes.Transparent, new Rect(view.Size));

        using (context.PushTransform(view.GraphToScreen))
        {
            if (!glass.SeeThrough) ground.DrawGrid(context);

            // The wires of modules being dragged are drawn after the modules, so
            // nothing the block is pulled across hides where it is still patched.
            var lifted = gestures.Carrying ? selection.Ids : new HashSet<Guid>();
            var scene = selection.Scene;

            // Under the modules and the wires both: the ground a group stands on.
            using (glass.Push(context)) groups.DrawOpenGroups(context, scene);

            shelves.Draw(context, Patch, scene, selection, onBoxes: false);

            using (glass.Push(context)) wires.DrawConnections(context, lifted, theirs: false, peeked: false);

            if (!gestures.Gesturing) marks.Draw(context);

            foreach (var node in Patch.Nodes)
                if (!scene.Shut(node.Id) && !scene.InPeek(node.Id) && !lift.Raised(node.Id) && NodeCatalog.Get(node.TypeId) is { } def)
                    modules.DrawNode(context, node, def);

            // A box being carried is drawn last of them, so it passes over the others.
            using (glass.Push(context))
            {
                foreach (var (group, sockets, bounds) in scene.Boxes())
                    if (!lift.Raised(group)) groups.DrawBox(context, group, sockets, bounds);

                foreach (var (group, sockets, bounds) in scene.Boxes())
                    if (lift.Raised(group)) groups.DrawBox(context, group, sockets, bounds);
            }

            shelves.Draw(context, Patch, scene, selection, onBoxes: true);

            // Held up over the boxes as well, so one carried across a shut box
            // is not hidden by it.
            foreach (var node in Patch.Nodes)
                if (!scene.Shut(node.Id) && !scene.InPeek(node.Id) && lift.Raised(node.Id) && NodeCatalog.Get(node.TypeId) is { } def)
                    modules.DrawNode(context, node, def);

            using (glass.Push(context)) wires.DrawConnections(context, lifted, theirs: true, peeked: false);

            if (!gestures.Gesturing) measured.Draw(context, peeked: false);

            using (glass.Push(context)) groups.DrawPeek(context, scene, lifted);

            using (glass.Push(context)) wires.DrawBusLinks(context);

            gestures.DrawPendingWire(context);

            dial.Draw(context, scene);
        }

        // Outside the transform, so a hairline stays a hairline and the dashes keep
        // their spacing at any zoom. Neither is part of the patch.
        if (!glass.SeeThrough) ground.DrawEdge(context);
        gestures.DrawMarquee(context);
        DrawOverlays(context);

        modules.KeepMoving();
    }

    /// <summary>The card over a value being turned or typed, at full strength even over the picture.</summary>
    private void DrawReadout(DrawingContext context)
    {
        if (focus.Reading is not var (pick, turn) || selection.Scene.RowOf(pick.Node, pick.Port) is not var (bounds, socket)) return;

        // Zoomed out the rows are not drawn, so the card points at the socket itself, ringed.
        var bare = geometry.Compact || view.Overview;
        var well = bare ? new Rect(socket, socket) : ValueWell.At(bounds, socket);
        var (card, above) = TurnReadout.Place(view.OnScreen(bounds), view.OnScreen(well), view.Size);

        TurnReadout.Draw(context, card, above, view.OnScreen(well).Center.X, turn);

        if (view.Overview) SocketTag.Draw(context, view.GraphToScreen.Transform(socket), turn.Name, turn.Hot);
    }

    /// <summary>A finger's tether, the card, the step bar and the list of knobs, over everything.</summary>
    private void DrawOverlays(DrawingContext context)
    {
        if (dial.Tether is var (home, finger))
        {
            context.DrawLine(TetherPen, home, finger);
            context.DrawEllipse(null, AnchorPen, home, 20, 20);
        }

        DrawReadout(context);
        bar.Draw(context);
        choice.Draw(context);
    }
}
