using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The inspector's row for a socket: an input's knob, or what is patched into
/// it or driving it with no wire; an output's name and what it feeds.
/// </summary>
internal sealed class SocketRows(NodeEditor editor, Document document, InspectorRows rows)
{
    /// <summary>
    /// A range typed into a linked row settled the panel's shape from here, so the
    /// inspector need not rebuild the row out from under the number being typed.
    /// </summary>
    public event EventHandler? Settled;

    /// <summary>
    /// A grouped module's unwired socket row, with the button that puts it on the
    /// box's edge: the counterpart of the ✕ on the group's panel.
    /// </summary>
    public Control Edged(Control row, NodeInstance node, int port, bool output)
    {
        var socket = new GroupSocket(node.Id, port, output);

        if (editor.History.Locked
            || editor.History.Patch.GroupOf(node.Id) is not { } group
            || !editor.History.Patch.Exposable(group, socket))
            return row;

        var expose = new Button
        {
            Name = "exposeSocket",
            Content = output ? "⇥" : "⇤",
            FontSize = Text.Caption,
            Padding = new Thickness(5, 0, 5, 0),
            Background = Brushes.Transparent,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(expose, $"Put this socket on the edge of “{group.Title()}”.");
        expose.Click += (_, _) => editor.Edits.ExposeSocket(group, socket);

        var edged = new DockPanel();

        DockPanel.SetDock(expose, Dock.Right);
        edged.Children.Add(expose);
        edged.Children.Add(row);

        return edged;
    }

    /// <summary>An output's name, and what it feeds beside it.</summary>
    public Grid Output(NodeInstance node, string name, int index)
    {
        var row = InspectorRows.Row("*");
        var caption = InspectorRows.Caption(name);
        var feeds = WireEnds.OutOf(editor.History.Patch, node.Id, index);

        caption.Margin = new Thickness(0, 2, 0, 2);
        row.Children.Add(caption);

        if (feeds is null)
        {
            caption.Width = double.NaN;
            Grid.SetColumnSpan(caption, 2);
        }
        else
        {
            var wired = new TextBlock
            {
                Text = feeds,
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(wired, 1);
            row.Children.Add(wired);
        }

        return row;
    }

    /// <param name="name">What the row is captioned, the socket's own name unless given.</param>
    public Control Input(NodeDef def, NodeInstance node, PortSpec spec, int index, bool reading, string? name = null)
    {
        name ??= spec.Name;

        var patched = WireEnds.Into(editor.History.Patch, node.Id, index);

        var label = InspectorRows.Caption(name);

        var row = InspectorRows.KnobRow(reading);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        if (patched is not null)
        {
            var wired = new TextBlock
            {
                Text = patched,
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(wired, 1);
            Grid.SetColumnSpan(wired, reading ? 3 : 2);
            row.Children.Add(wired);
            return row;
        }

        // A normalled socket has no knob to show. It is already carrying a
        // signal — one the patch does not draw, because there is no module on
        // the canvas for a wire to come from — so the row says which, in the
        // place a slider would have been. Why it is not a slider is the whole
        // point of it: there is nothing to set here until something is patched
        // in, and a control that did nothing would be worse than none.
        if (NodeCatalog.Normalled(spec) is { } normalled)
        {
            var implied = new TextBlock
            {
                Text = $"◀ {normalled}, without a wire",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(implied, 1);
            Grid.SetColumnSpan(implied, reading ? 3 : 2);
            row.Children.Add(implied);
            return row;
        }

        // The other kind of normalled jack: an earlier socket on the same
        // module rather than a hidden one off it. Output's 'right' falls back
        // to 'left' this way, and the row names it exactly as it would a
        // module normalled off the canvas.
        if (spec.NormalledFrom is >= 0 and var from && from < def.Inputs.Count)
        {
            var implied = new TextBlock
            {
                Text = $"◀ {def.Inputs[from].Name}, without a wire",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(implied, 1);
            Grid.SetColumnSpan(implied, reading ? 3 : 2);
            row.Children.Add(implied);
            return row;
        }

        // A socket with nothing worth a knob — see PortSpec.NeedsAWire. The
        // stored default still answers the compiler when nothing is patched,
        // it is just not a number anybody chose by dragging, so the row says
        // that plainly instead of offering a slider that would mislead.
        if (spec.NeedsAWire)
        {
            var unpatched = new TextBlock
            {
                Text = "◀ not patched",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(unpatched, 1);
            Grid.SetColumnSpan(unpatched, reading ? 3 : 2);
            row.Children.Add(unpatched);
            return row;
        }

        if (ControlMap.Of(node, index) is { } link && editor.History.Patch.Control(link.Control) is { } knob)
            return LinkedRow(node, spec, name, index, link, knob);

        var value = index < node.InputValues.Length ? node.InputValues[index] : spec.Default;
        var (reads, flag) = RemapReading(def, node, index);

        // Named after the socket, so a slider dragged across its range is one
        // step to undo rather than one per frame of the drag.
        return rows.ValueRow(name, spec, value, $"{node.Id} input {index}", next =>
        {
            if (index < node.InputValues.Length) node.InputValues[index] = next;

            // Noted rather than written. A drag is a knob turned a hundred times
            // and the text should be edited once, when the hand comes off it.
            document.Turned(node.Id, index);
        }, reading, reads, flag);
    }

    /// <summary>
    /// For one of an Auto remap's range knobs, what its fraction comes to at the
    /// far end of the wire, or why the pair is plain numbers; nothing for any other socket.
    /// </summary>
    private (Func<float, string>? Reads, string? Flag) RemapReading(NodeDef def, NodeInstance node, int index)
    {
        if (def.TypeId != NodeCatalog.AutoRemapTypeId || index == AutoRemap.In) return (null, null);

        var spans = AutoRemap.Of(editor.History.Patch, node);
        var input = index is AutoRemap.InLow or AutoRemap.InHigh;

        // Fractions of 0..1 are the numbers themselves, so an unwired side says nothing more.
        return (input ? spans.In : spans.Out) switch
        {
            { } span when span == RemapSpan.Unit => (null, null),
            { } span => (travel => span.Format(travel), null),
            null => (_ => "", $"Plain numbers: {(input ? spans.InWhy : spans.OutWhy)}."),
        };
    }

    /// <summary>
    /// A socket that follows a knob, in the inspector: which knob, the range it
    /// follows it over, and a button to let it go.
    /// </summary>
    private Control LinkedRow(NodeInstance node, PortSpec spec, string caption, int index, ControlLink link, PatchControl knob)
    {
        var row = InspectorRows.Row("*,58,14,58,26");

        var label = InspectorRows.Caption(caption);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var attention = new SolidColorBrush(Colors.Attention);

        var name = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var ring = Glyphs.Linked(12, attention);
        ring.Margin = new Thickness(0, 0, 4, 0);
        DockPanel.SetDock(ring, Dock.Left);
        name.Children.Add(ring);
        name.Children.Add(new TextBlock
        {
            Text = knob.Name,
            FontSize = Text.Body,
            Foreground = attention,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        ToolTip.SetTip(name, $"Follows the knob '{knob.Name}' on the knob panel, over this range.");

        var min = Bound(link.Min, next => link with { Min = next });
        var max = Bound(link.Max, next => link with { Max = next });

        var dash = new TextBlock
        {
            Text = "–",
            Foreground = Text.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var unlink = new Button
        {
            Name = "unlink",
            Content = Glyphs.Cross(12),
            Padding = new Thickness(0),
            Width = 22,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        ToolTip.SetTip(unlink, "Let this socket go, leaving it where the knob had put it.");

        unlink.Click += (_, _) =>
        {
            if (index < node.InputValues.Length) node.InputValues[index] = link.At(knob.Value);

            ControlMap.Unlink(node, index);
            editor.History.Record();
        };

        Grid.SetColumn(name, 1);
        Grid.SetColumn(min, 2);
        Grid.SetColumn(dash, 3);
        Grid.SetColumn(max, 4);
        Grid.SetColumn(unlink, 5);

        row.Children.Add(name);
        row.Children.Add(min);
        row.Children.Add(dash);
        row.Children.Add(max);
        row.Children.Add(unlink);

        return row;

        NumericUpDown Bound(float value, Func<float, ControlLink> with)
        {
            var box = new NumericUpDown
            {
                Value = Boxed.Of(value),
                Increment = spec.Stepped ? 1m : 0.05m,
                FormatString = spec.Stepped ? "0.##" : "0.###",
                FontSize = Text.Body,
                ShowButtonSpinner = false,
                VerticalAlignment = VerticalAlignment.Center,
            };

            box.ValueChanged += (_, e) =>
            {
                if (e.NewValue is not { } next) return;

                link = with((float)next);
                ControlMap.Link(node, index, link);

                // The range is part of what the panel takes its shape from, so
                // that one changed from elsewhere rebuilds this row. Changed from
                // here the row already says it, and rebuilding would take the box
                // out from under the number being typed into it — after its
                // first digit, a box taking its value a keystroke at a time.
                Settled?.Invoke(this, EventArgs.Empty);

                editor.History.Record($"{node.Id} range {index}");
            };

            return Boxed.NeverBlank(box);
        }
    }
}
