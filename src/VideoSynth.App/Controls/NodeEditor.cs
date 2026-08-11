using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VideoSynth.Core.Graph;

namespace VideoSynth.App.Controls;

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
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2C)), 1);
    private static readonly IPen GridPenMajor = new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x30, 0x36)), 1);
    private static readonly IPen NodeBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x18)), 1.5);
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40)), 2);
    private static readonly IPen PortOutline = new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x18)), 1.2);

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PortCursor = new(StandardCursorType.Cross);
    private static readonly Cursor NodeCursor = new(StandardCursorType.SizeAll);

    private Patch _patch = new();
    private double _zoom = 1;
    private Point _pan = new(40, 40);
    private Guid? _selected;

    private bool _framePending = true;
    private Drag _drag;
    private Point _dragOrigin;
    private Point _nodeOrigin;
    private Guid _wireNode;
    private int _wirePort;
    private bool _wireFromOutput;
    private Point _wireEnd;

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
        get => _patch;
        set
        {
            _patch = value;
            _selected = null;
            _drag = Drag.None;
            FrameAll();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            PatchChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NodeInstance? SelectedNode => _selected is { } id ? _patch.Find(id) : null;

    /// <summary>Call after editing a node from outside the canvas, e.g. the inspector.</summary>
    public void NotifyPatchChanged()
    {
        InvalidateVisual();
        PatchChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops a new module in the middle of the current view.</summary>
    public NodeInstance AddNode(string typeId)
    {
        var def = NodeCatalog.Require(typeId);
        var centre = ToGraph(new Point(Bounds.Width / 2, Bounds.Height / 2));

        var node = NodeInstance.Create(def, centre.X - NodeGeometry.Width / 2, centre.Y - NodeGeometry.Height(def) / 2);
        _patch.Nodes.Add(node);
        Select(node.Id);
        NotifyPatchChanged();
        return node;
    }

    public void DeleteSelected()
    {
        if (_selected is not { } id) return;

        _patch.Remove(id);
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
            _framePending = true;
            return;
        }

        _framePending = false;

        if (_patch.Nodes.Count == 0)
        {
            _zoom = 1;
            _pan = new Point(40, 40);
            InvalidateVisual();
            return;
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

        foreach (var node in _patch.Nodes)
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

        _zoom = Math.Clamp(Math.Min(scaleX, scaleY), 0.2, 1.4);
        _pan = new Point(
            (Bounds.Width - (right - left) * _zoom) / 2 - left * _zoom,
            (Bounds.Height - (bottom - top) * _zoom) / 2 - top * _zoom);

        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (_framePending) FrameAll();
    }

    // --- coordinate transforms ----------------------------------------------

    private Matrix GraphToScreen =>
        Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y);

    private Point ToGraph(Point screen) =>
        new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

    // --- painting ------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));

        using (context.PushTransform(GraphToScreen))
        {
            DrawGrid(context);
            DrawConnections(context);

            foreach (var node in _patch.Nodes)
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
        foreach (var connection in _patch.Connections)
        {
            var source = _patch.Find(connection.SourceNode);
            var target = _patch.Find(connection.TargetNode);
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
        if (_drag != Drag.Wire) return;
        if (_patch.Find(_wireNode) is not { } node) return;
        if (NodeCatalog.Get(node.TypeId) is not { } def) return;

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x40), 0.9), 2.2, DashStyle.Dash);

        if (_wireFromOutput)
            DrawWire(context, NodeGeometry.OutputPort(node, _wirePort), _wireEnd, pen);
        else
            DrawWire(context, _wireEnd, NodeGeometry.InputPort(node, def, _wirePort), pen);
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
        var isSelected = _selected == node.Id;
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
            var connected = _patch.IncomingTo(node.Id, i) is not null;

            var label = Text(port.Name, 11.5, LabelBrush, bounds.Width * 0.55, true);
            context.DrawText(label, new Point(bounds.X + 14, centre.Y - label.Height / 2));

            // An unconnected input shows the knob value it will compile to.
            if (!connected && i < node.InputValues.Length)
            {
                var value = Text(Format(node.InputValues[i]), 11.5, ValueBrush, bounds.Width * 0.4, true);
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

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // --- interaction ---------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var properties = e.GetCurrentPoint(this).Properties;
        var screen = e.GetPosition(this);
        var graph = ToGraph(screen);
        _dragOrigin = screen;

        if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed)
        {
            _drag = Drag.Pan;
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
            _drag = Drag.Node;
            _nodeOrigin = new Point(node.X, node.Y);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        Select(null);
        _drag = Drag.Pan;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    /// <summary>
    /// Grabbing a connected input picks the existing wire up by its far end,
    /// so re-patching works the way it does on a real rig.
    /// </summary>
    private void StartWire(Guid nodeId, int portIndex, bool isOutput, Point graph)
    {
        if (!isOutput && _patch.IncomingTo(nodeId, portIndex) is { } existing)
        {
            _patch.Disconnect(nodeId, portIndex);
            _wireNode = existing.SourceNode;
            _wirePort = existing.SourcePort;
            _wireFromOutput = true;
            PatchChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _wireNode = nodeId;
            _wirePort = portIndex;
            _wireFromOutput = isOutput;
        }

        _drag = Drag.Wire;
        _wireEnd = graph;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var screen = e.GetPosition(this);
        var graph = ToGraph(screen);

        switch (_drag)
        {
            case Drag.Pan:
                _pan += screen - _dragOrigin;
                _dragOrigin = screen;
                InvalidateVisual();
                return;

            case Drag.Node when SelectedNode is { } node:
                var delta = (screen - _dragOrigin) / _zoom;
                node.X = _nodeOrigin.X + delta.X;
                node.Y = _nodeOrigin.Y + delta.Y;
                InvalidateVisual();
                return;

            case Drag.Wire:
                _wireEnd = graph;
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

        if (_drag == Drag.Wire)
            CompleteWire(ToGraph(e.GetPosition(this)));

        _drag = Drag.None;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    private void CompleteWire(Point graph)
    {
        if (!HitPort(graph, out var node, out var port, out var isOutput)) return;

        // A wire only means something between opposite kinds of socket.
        if (isOutput == _wireFromOutput) return;

        if (_wireFromOutput)
            _patch.Connect(_wireNode, _wirePort, node, port);
        else
            _patch.Connect(node, port, _wireNode, _wirePort);

        PatchChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var screen = e.GetPosition(this);
        var anchor = ToGraph(screen);

        _zoom = Math.Clamp(_zoom * Math.Pow(1.12, e.Delta.Y), 0.2, 3.0);

        // Keep whatever was under the cursor pinned there.
        _pan = new Point(screen.X - anchor.X * _zoom, screen.Y - anchor.Y * _zoom);

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
        for (var i = _patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = _patch.Nodes[i];
            var def = NodeCatalog.Get(node.TypeId);

            if (def is not null && NodeGeometry.Bounds(node, def).Contains(graph))
                return node;
        }

        return null;
    }

    private bool HitPort(Point graph, out Guid nodeId, out int portIndex, out bool isOutput)
    {
        var tolerance = NodeGeometry.PortRadius + NodeGeometry.HitPadding;

        for (var i = _patch.Nodes.Count - 1; i >= 0; i--)
        {
            var node = _patch.Nodes[i];
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
        if (_selected == id) return;

        _selected = id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Nodes paint in list order, so the last one drawn is the one on top.</summary>
    private void BringToFront(NodeInstance node)
    {
        _patch.Nodes.Remove(node);
        _patch.Nodes.Add(node);
    }
}
