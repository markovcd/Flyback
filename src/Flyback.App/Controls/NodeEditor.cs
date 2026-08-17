using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The patch bay. Everything is drawn directly rather than built from controls,
/// which keeps panning and zooming over a few hundred modules cheap and puts
/// layout, painting and hit-testing in one place.
/// </summary>
public sealed class NodeEditor : Control
{
    private enum Drag
    {
        None,
        Pan,
        Node,
        Wire,
    }

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x20));
    private static readonly IBrush NodeFill = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x34));
    private static readonly IBrush NodeFillSelected = new SolidColorBrush(Color.FromRgb(0x32, 0x36, 0x3E));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4));
    private static readonly IBrush ValueBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x92, 0xA0));
    private static readonly IBrush HeaderTextBrush = Brushes.White;
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2C)));
    private static readonly IPen GridPenMajor = new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x30, 0x36)));
    private static readonly IPen NodeBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x18)), 1.5);
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40)), 2);
    private static readonly IPen PortOutline = new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x18)), 1.2);

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PortCursor = new(StandardCursorType.Cross);
    private static readonly Cursor NodeCursor = new(StandardCursorType.SizeAll);

    private Patch patch = new();
    private double zoom = 1;
    private Point pan = new(40, 40);
    private Guid? selected;

    private bool framePending = true;
    private Drag drag;
    private Point dragOrigin;
    private Point nodeOrigin;
    private Guid wireNode;
    private int wirePort;
    private bool wireFromOutput;
    private Point wireEnd;

    public NodeEditor()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Raised whenever the graph itself changed and needs recompiling.</summary>
    public event EventHandler? PatchChanged;

    /// <summary>Raised when a different node becomes selected.</summary>
    public event EventHandler? SelectionChanged;

    public Patch Patch
    {
        get => patch;
        set
        {
            patch = value;
            selected = null;
            drag = Drag.None;
            FrameAll();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            PatchChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NodeInstance? SelectedNode => selected is { } id ? patch.Find(id) : null;

    /// <summary>Call after editing a node from outside the canvas, e.g. the inspector.</summary>
    public void NotifyPatchChanged()
    {
        InvalidateVisual();
        PatchChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops a new module in the middle of the current view and hands it back,
    /// or returns null having added nothing where the patch may hold only one of
    /// that module — the two sinks. Rather than do nothing at all, that case
    /// selects the one already there: whoever asked for that module wanted it,
    /// and this is where it is.
    /// </summary>
    public NodeInstance? AddNode(string typeId)
    {
        var def = NodeCatalog.Require(typeId);

        if (!patch.CanAdd(typeId))
        {
            if (patch.FirstOf(typeId) is { } already) Select(already.Id);

            InvalidateVisual();
            return null;
        }

        var centre = ToGraph(new Point(Bounds.Width / 2, Bounds.Height / 2));

        var node = NodeInstance.Create(def, centre.X - NodeGeometry.Width / 2, centre.Y - NodeGeometry.Height(def) / 2);
        patch.Nodes.Add(node);
        Select(node.Id);
        NotifyPatchChanged();
        return node;
    }

    public void DeleteSelected()
    {
        if (selected is not { } id) return;

        patch.Remove(id);
        Select(null);
        NotifyPatchChanged();
    }

    /// <summary>
    /// Fits every node into view. A patch is usually loaded before the control
    /// has been measured, so this defers until there is a viewport to fit into.
    /// </summary>
    public void FrameAll()
    {
        if (Bounds.Width < 1 || Bounds.Height < 1)
        {
            framePending = true;
            return;
        }

        framePending = false;

        if (patch.Nodes.Count == 0)
        {
            zoom = 1;
            pan = new Point(40, 40);
            InvalidateVisual();
            return;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

        foreach (var node in patch.Nodes)
        {
            var def = NodeCatalog.Get(node.TypeId);
            if (def is null) continue;

            var bounds = NodeGeometry.Bounds(node, def);
            left = Math.Min(left, bounds.Left);
            top = Math.Min(top, bounds.Top);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        if (left > right) return;

        const double margin = 50;
        var scaleX = Bounds.Width / (right - left + margin * 2);
        var scaleY = Bounds.Height / (bottom - top + margin * 2);

        zoom = Math.Clamp(Math.Min(scaleX, scaleY), 0.2, 1.4);
        pan = new Point(
            (Bounds.Width - (right - left) * zoom) / 2 - left * zoom,
            (Bounds.Height - (bottom - top) * zoom) / 2 - top * zoom);

        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (framePending) FrameAll();
    }

    // --- coordinate transforms ----------------------------------------------

    private Matrix GraphToScreen =>
        Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(pan.X, pan.Y);

    private Point ToGraph(Point screen) =>
        new((screen.X - pan.X) / zoom, (screen.Y - pan.Y) / zoom);

    // --- painting ------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));

        using (context.PushTransform(GraphToScreen))
        {
            DrawGrid(context);
            DrawConnections(context);

            foreach (var node in patch.Nodes)
                if (NodeCatalog.Get(node.TypeId) is { } def)
                    DrawNode(context, node, def);

            DrawPendingWire(context);
        }
    }

    private void DrawGrid(DrawingContext context)
    {
        var topLeft = ToGraph(new Point(0, 0));
        var bottomRight = ToGraph(new Point(Bounds.Width, Bounds.Height));

        const double spacing = 48;

        // Zoomed far out the grid stops being useful and only costs draw calls.
        if ((bottomRight.X - topLeft.X) / spacing > 400) return;

        var firstX = Math.Floor(topLeft.X / spacing) * spacing;
        var firstY = Math.Floor(topLeft.Y / spacing) * spacing;

        for (var x = firstX; x <= bottomRight.X; x += spacing)
        {
            var pen = Math.Abs(x % (spacing * 5)) < 0.5 ? GridPenMajor : GridPen;
            context.DrawLine(pen, new Point(x, topLeft.Y), new Point(x, bottomRight.Y));
        }

        for (var y = firstY; y <= bottomRight.Y; y += spacing)
        {
            var pen = Math.Abs(y % (spacing * 5)) < 0.5 ? GridPenMajor : GridPen;
            context.DrawLine(pen, new Point(topLeft.X, y), new Point(bottomRight.X, y));
        }
    }

    private void DrawConnections(DrawingContext context)
    {
        foreach (var connection in patch.Connections)
        {
            var source = patch.Find(connection.SourceNode);
            var target = patch.Find(connection.TargetNode);
            if (source is null || target is null) continue;

            var sourceDef = NodeCatalog.Get(source.TypeId);
            var targetDef = NodeCatalog.Get(target.TypeId);
            if (sourceDef is null || targetDef is null) continue;
            if (connection.SourcePort >= sourceDef.Outputs.Count) continue;
            if (connection.TargetPort >= targetDef.Inputs.Count) continue;

            var from = NodeGeometry.OutputPort(source, connection.SourcePort);
            var to = NodeGeometry.InputPort(target, targetDef, connection.TargetPort);
            var colour = NodeGeometry.PortColour(sourceDef.Outputs[connection.SourcePort].Kind);

            DrawWire(context, from, to, new Pen(new SolidColorBrush(colour, 0.85), 2.2));
        }
    }

    private void DrawPendingWire(DrawingContext context)
    {
        if (drag != Drag.Wire) return;
        if (patch.Find(wireNode) is not { } node) return;
        if (NodeCatalog.Get(node.TypeId) is not { } def) return;

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40), 0.9), 2.2, DashStyle.Dash);

        if (wireFromOutput)
            DrawWire(context, NodeGeometry.OutputPort(node, wirePort), wireEnd, pen);
        else
            DrawWire(context, wireEnd, NodeGeometry.InputPort(node, def, wirePort), pen);
    }

    /// <summary>A horizontal-tangent bezier, so wires leave and enter sockets cleanly.</summary>
    private static void DrawWire(DrawingContext context, Point from, Point to, IPen pen)
    {
        var reach = Math.Max(45, Math.Abs(to.X - from.X) * 0.5);

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(from, false);
            sink.CubicBezierTo(from.WithX(from.X + reach), to.WithX(to.X - reach), to);
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawNode(DrawingContext context, NodeInstance node, NodeDef def)
    {
        var bounds = NodeGeometry.Bounds(node, def);
        var isSelected = selected == node.Id;
        var accent = NodeGeometry.Accent(def.Category);

        context.DrawRectangle(
            isSelected ? NodeFillSelected : NodeFill,
            isSelected ? SelectionPen : NodeBorder,
            new RoundedRect(bounds, NodeGeometry.CornerRadius));

        // Header band, square at the bottom so it reads as a title bar.
        var header = new Rect(bounds.X, bounds.Y, bounds.Width, NodeGeometry.HeaderHeight);
        context.DrawRectangle(
            new SolidColorBrush(accent, 0.85),
            null,
            new RoundedRect(header, NodeGeometry.CornerRadius, NodeGeometry.CornerRadius, 0, 0));

        context.DrawText(
            Text(def.Name, 12.5, HeaderTextBrush, bounds.Width - 16, true),
            new Point(bounds.X + 9, bounds.Y + 5));

        for (var i = 0; i < def.Outputs.Count; i++)
        {
            var port = def.Outputs[i];
            var centre = NodeGeometry.OutputPort(node, i);
            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width - 24, true);

            context.DrawText(label, new Point(bounds.Right - 14 - label.Width, centre.Y - label.Height / 2));
            DrawPort(context, centre, port.Kind);
        }

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            var port = def.Inputs[i];
            var centre = NodeGeometry.InputPort(node, def, i);
            var connected = patch.IncomingTo(node.Id, i) is not null;

            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width * 0.55, true);
            context.DrawText(label, new Point(bounds.X + 14, centre.Y - label.Height / 2));

            // An unconnected input shows the knob value it will compile to.
            if (!connected && i < node.InputValues.Length)
            {
                var value = Text(port.Format(node.InputValues[i]), 11.5, ValueBrush, bounds.Width * 0.4, true);
                context.DrawText(value, new Point(bounds.Right - 12 - value.Width, centre.Y - value.Height / 2));
            }

            DrawPort(context, centre, port.Kind);
        }
    }

    private static void DrawPort(DrawingContext context, Point centre, PortKind kind) =>
        context.DrawEllipse(
            new SolidColorBrush(NodeGeometry.PortColour(kind)),
            PortOutline,
            centre,
            NodeGeometry.PortRadius,
            NodeGeometry.PortRadius);

    private static FormattedText Text(string text, double size, IBrush brush, double maxWidth, bool trim)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);

        if (trim)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    // --- interaction ---------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var properties = e.GetCurrentPoint(this).Properties;
        var screen = e.GetPosition(this);
        var graph = ToGraph(screen);
        dragOrigin = screen;

        if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed)
        {
            drag = Drag.Pan;
            e.Pointer.Capture(this);
            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        if (HitPort(graph, out var portNode, out var portIndex, out var isOutput))
        {
            StartWire(portNode, portIndex, isOutput, graph);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        if (HitNode(graph) is { } node)
        {
            Select(node.Id);
            BringToFront(node);
            drag = Drag.Node;
            nodeOrigin = new Point(node.X, node.Y);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        Select(null);
        drag = Drag.Pan;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    /// <summary>
    /// Grabbing a connected input picks the existing wire up by its far end,
    /// so re-patching works the way it does on a real rig.
    /// </summary>
    private void StartWire(Guid nodeId, int portIndex, bool isOutput, Point graph)
    {
        if (!isOutput && patch.IncomingTo(nodeId, portIndex) is { } existing)
        {
            patch.Disconnect(nodeId, portIndex);
            wireNode = existing.SourceNode;
            wirePort = existing.SourcePort;
            wireFromOutput = true;
            PatchChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            wireNode = nodeId;
            wirePort = portIndex;
            wireFromOutput = isOutput;
        }

        drag = Drag.Wire;
        wireEnd = graph;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var screen = e.GetPosition(this);
        var graph = ToGraph(screen);

        switch (drag)
        {
            case Drag.Pan:
                pan += screen - dragOrigin;
                dragOrigin = screen;
                InvalidateVisual();
                return;

            case Drag.Node when SelectedNode is { } node:
                var delta = (screen - dragOrigin) / zoom;
                node.X = nodeOrigin.X + delta.X;
                node.Y = nodeOrigin.Y + delta.Y;
                InvalidateVisual();
                return;

            case Drag.Wire:
                wireEnd = graph;
                InvalidateVisual();
                return;

            default:
                Cursor = HitPort(graph, out _, out _, out _) ? PortCursor
                    : HitNode(graph) is not null ? NodeCursor
                    : ArrowCursor;
                return;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (drag == Drag.Wire)
            CompleteWire(ToGraph(e.GetPosition(this)));

        drag = Drag.None;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void CompleteWire(Point graph)
    {
        if (!HitPort(graph, out var node, out var port, out var isOutput)) return;

        // A wire only means something between opposite kinds of socket.
        if (isOutput == wireFromOutput) return;

        if (wireFromOutput)
            patch.Connect(wireNode, wirePort, node, port);
        else
            patch.Connect(node, port, wireNode, wirePort);

        PatchChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var screen = e.GetPosition(this);
        var anchor = ToGraph(screen);

        zoom = Math.Clamp(zoom * Math.Pow(1.12, e.Delta.Y), 0.2, 3.0);

        // Keep whatever was under the cursor pinned there.
        pan = new Point(screen.X - anchor.X * zoom, screen.Y - anchor.Y * zoom);

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                DeleteSelected();
                e.Handled = true;
                break;

            case Key.F:
                FrameAll();
                e.Handled = true;
                break;
        }
    }

    // --- hit testing (all in graph space) ------------------------------------

    private NodeInstance? HitNode(Point graph)
    {
        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);

            if (def is not null && NodeGeometry.Bounds(node, def).Contains(graph))
                return node;
        }

        return null;
    }

    private bool HitPort(Point graph, out Guid nodeId, out int portIndex, out bool isOutput)
    {
        var tolerance = NodeGeometry.PortRadius + NodeGeometry.HitPadding;

        for (var i = patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);
            if (def is null) continue;

            for (var p = 0; p < def.Outputs.Count; p++)
            {
                if (!Near(NodeGeometry.OutputPort(node, p), graph, tolerance)) continue;

                (nodeId, portIndex, isOutput) = (node.Id, p, true);
                return true;
            }

            for (var p = 0; p < def.Inputs.Count; p++)
            {
                if (!Near(NodeGeometry.InputPort(node, def, p), graph, tolerance)) continue;

                (nodeId, portIndex, isOutput) = (node.Id, p, false);
                return true;
            }
        }

        (nodeId, portIndex, isOutput) = (Guid.Empty, -1, false);
        return false;
    }

    private static bool Near(Point a, Point b, double tolerance)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy <= tolerance * tolerance;
    }

    private void Select(Guid? id)
    {
        if (selected == id) return;

        selected = id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Nodes paint in list order, so the last one drawn is the one on top.</summary>
    private void BringToFront(NodeInstance node)
    {
        patch.Nodes.Remove(node);
        patch.Nodes.Add(node);
    }
}
