using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>The wires between modules, and the dotted line joining a selected Send to its Receives.</summary>
internal sealed class WirePainter(
    CanvasHistory history,
    CanvasSelection selection,
    NodeGeometry geometry,
    CanvasLift lift,
    CanvasGlass glass,
    Viewport view)
{
    /// <summary>How thick a wire is drawn, and how far under full strength.</summary>
    private const double WireThickness = 2.2;

    private const double RestingWireOpacity = 0.85;

    /// <summary>
    /// A wire on the module being dragged. Heavier rather than differently
    /// colored, because a wire's color already says what flows down it and
    /// that is not what has changed about this one.
    /// </summary>
    private const double LiftedWireThickness = 3.4;

    /// <summary>
    /// How much wider than its line a polyphonic wire's band is, and how strongly
    /// the band is drawn: a cable of several signals, under the one line every
    /// wire has, which works over any ground a group lays under it.
    /// </summary>
    private const double VoicesBand = 2.6, VoicesBandOpacity = 0.45;

    /// <summary>
    /// How strongly a wire leaving a box being looked into is drawn at its far end,
    /// against full strength where it leaves the box. It reaches nothing, so no edge
    /// is left against the socket it runs into.
    /// </summary>
    private const double PeekWireFar = 0, PeekWireNear = 0.7;

    private static readonly IPen BusPen = new ImmutablePen(
        new ImmutableSolidColorBrush(Colors.Attention, 0.7),
        1.5,
        new ImmutableDashStyle([1, 3], 0));

    /// <summary>A loop's wire: <see cref="DashStyle.Dash"/>, made once so each wire's pen can be immutable.</summary>
    private static readonly ImmutableDashStyle LoopDashes = new(DashStyle.Dash.Dashes, DashStyle.Dash.Offset);

    private Patch Patch => history.Patch;

    /// <param name="context">Where the canvas is drawing.</param>
    /// <param name="lifted">
    /// The modules being dragged, empty while none are. A set rather than one id
    /// because a drag may carry a whole selection.
    /// </param>
    /// <param name="theirs">
    /// Which half of the wires this pass draws: those modules' own, or all the rest.
    /// One loop serves both, so the two passes partition the same set rather than each
    /// deciding what belongs in it.
    /// </param>
    /// <param name="peeked">
    /// Whether this pass draws the wires of the box being looked into, which are
    /// drawn over everything, or the rest.
    /// </param>
    internal void DrawConnections(DrawingContext context, IReadOnlySet<Guid> lifted, bool theirs, bool peeked)
    {
        // What a loop is made of, and the one thing about a wire the canvas
        // cannot read off its two ends — see Cycles.Backwards, which the compiler
        // asks the same question of.
        var backwards = Cycles.BackwardsThroughBuses(Patch);
        var scene = selection.Scene;

        // Which wires carry voices, asked of the patch the compiler sees — see
        // VoiceCounts — so a chain beyond a bus is drawn as what it carries.
        var voices = VoiceCounts.Of(Buses.Joined(Patch), NodeCatalog.Current);

        foreach (var connection in Patch.Connections)
        {
            var mine = lifted.Contains(connection.SourceNode)
                || lifted.Contains(connection.TargetNode);

            if (mine != theirs) continue;
            if ((scene.InPeek(connection.SourceNode) || scene.InPeek(connection.TargetNode)) != peeked) continue;

            // A wire with both ends inside one collapsed box is a wire the box
            // is standing in front of. Not drawn faintly or routed around — it
            // is simply not on the canvas while the box is shut.
            if (scene.Hidden(connection)) continue;

            var source = Patch.Find(connection.SourceNode);
            var target = Patch.Find(connection.TargetNode);
            if (source is null || target is null) continue;

            var sourceDef = NodeCatalog.Get(source.TypeId);
            var targetDef = NodeCatalog.Get(target.TypeId);
            if (sourceDef is null || targetDef is null) continue;
            if (connection.SourcePort >= sourceDef.Outputs.Count) continue;
            if (connection.TargetPort >= targetDef.Inputs.Count) continue;

            var from = lift.Held(source.Id, scene.OutputAnchor(source, connection.SourcePort));
            var to = lift.Held(target.Id, scene.InputAnchor(target, targetDef, connection.TargetPort));
            var returning = from.X > to.X;
            var run = returning ? WirePath.ReturnRun(scene.RouteBounds(source, sourceDef), scene.RouteBounds(target, targetDef)) : 0;

            if (!view.Shows(returning ? WirePath.ReturnExtent(from, to, run) : WirePath.Extent(from, to))) continue;

            // A wire swinging past the range its socket takes is drawn in the
            // accent, which is where the status bar's warning about it points.
            var color = AutoRemap.Overflow(Patch, connection) is null
                ? Colors.PortColor(sourceDef.Outputs[connection.SourcePort].Kind)
                : Colors.Attention;

            // A wire onto or off a module that is switched off is drawn as faintly
            // as the module is: what it shows is where the patch runs again once
            // that module comes back.
            var strength = source.Off || target.Off ? ModuleFace.OffOpacity : 1;

            // Dashed where the wire runs backwards, which is the whole of how a
            // loop shows itself: what this one carries is the evaluation before,
            // and a solid wire would say it carried this one.
            var dashes = backwards.Contains(connection) ? LoopDashes : null;

            // Heavier and at full strength, which is the same signal the pending
            // wire gives: this one is in play.
            var opacity = theirs ? strength : RestingWireOpacity * strength;

            // A wire leaving a box being looked into fades toward the dimmed canvas
            // it runs off into, measured end to end rather than along the curve.
            var leaving = peeked && scene.InPeek(connection.SourceNode) != scene.InPeek(connection.TargetNode);

            IBrush ink = leaving && from != to
                ? Fading(color, opacity, scene.InPeek(connection.SourceNode) ? (from, to) : (to, from))
                : new ImmutableSolidColorBrush(color, opacity);

            var thickness = theirs ? LiftedWireThickness : WireThickness;
            IPen pen = ink is IImmutableBrush still ? new ImmutablePen(still, thickness, dashes) : new Pen(ink, thickness, dashes);
            var count = voices.GetValueOrDefault(connection.SourceNode, 1);

            if (glass.SeeThrough) Route(new ImmutablePen(CanvasGlass.Casing, thickness + 2.5, dashes));

            if (count > 1)
            {
                Route(new ImmutablePen(new ImmutableSolidColorBrush(color, opacity * VoicesBandOpacity), thickness * VoicesBand, dashes));
                DrawVoiceCount(context, from, count, color, opacity);
            }

            Route(pen);

            // How a wire is routed is a question of where its ends are, not of
            // what it carries: one that has to travel leftwards goes round, and a
            // loop whose modules are laid out left to right is drawn like any
            // other chain. The dashes are what say which wire is the cut.
            void Route(IPen drawn)
            {
                if (returning)
                {
                    WirePath.DrawReturn(context, from, to, run, drawn);
                    return;
                }

                WirePath.Draw(context, from, to, drawn);
            }
        }
    }

    /// <summary>How many voices a polyphonic wire carries, just past the output it leaves.</summary>
    private static void DrawVoiceCount(DrawingContext context, Point from, int count, Color color, double opacity)
    {
        var text = CanvasText.Text($"×{count}", CanvasText.RowSize, new ImmutableSolidColorBrush(color, opacity), 40, false);

        context.DrawText(text, new Point(from.X + 8, from.Y - text.Height - 2));
    }

    /// <summary>A wire's ink, strongest at <paramref name="ends"/>'s first point and faint at its second.</summary>
    private static LinearGradientBrush Fading(Color color, double opacity, (Point Near, Point Far) ends) => new()
    {
        StartPoint = new RelativePoint(ends.Near, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(ends.Far, RelativeUnit.Absolute),
        GradientStops =
        [
            new GradientStop(Colors.Faded(color, opacity * PeekWireNear), 0),
            new GradientStop(Colors.Faded(color, opacity * PeekWireFar), 1),
        ],
    };

    internal void DrawBusLinks(DrawingContext context)
    {
        foreach (var id in selection.Ids)
        {
            if (Patch.Find(id) is not { } end || NodeCatalog.BusOf(end) is not { } bus) continue;
            if (selection.Scene.Shut(end.Id) || NodeCatalog.Get(end.TypeId) is not { } def) continue;

            var partner = end.TypeId == NodeCatalog.SendTypeId ? NodeCatalog.ReceiveTypeId : NodeCatalog.SendTypeId;
            var from = geometry.Bounds(end, def);

            foreach (var other in Patch.Nodes)
            {
                if (other.TypeId != partner || selection.Scene.Shut(other.Id)) continue;
                if (!string.Equals(NodeCatalog.BusOf(other), bus, StringComparison.OrdinalIgnoreCase)) continue;
                if (NodeCatalog.Get(other.TypeId) is not { } otherDef) continue;

                var to = geometry.Bounds(other, otherDef);

                context.DrawLine(BusPen, Edge(from, to.Center), Edge(to, from.Center));
            }
        }
    }

    /// <summary>Where the line from the middle of <paramref name="box"/> towards <paramref name="toward"/> leaves it.</summary>
    private static Point Edge(Rect box, Point toward)
    {
        var d = toward - box.Center;

        if (d.X == 0 && d.Y == 0) return box.Center;

        var t = Math.Min(
            d.X == 0 ? double.MaxValue : box.Width / 2 / Math.Abs(d.X),
            d.Y == 0 ? double.MaxValue : box.Height / 2 / Math.Abs(d.Y));

        return box.Center + d * Math.Min(t, 1);
    }
}
