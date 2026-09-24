using System.Text;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>What the inspector's rows are, as against what is in them, as one string to compare.</summary>
internal static class InspectorShape
{
    /// <summary>
    /// The selected module and what each of its sockets is patched to, as one string to
    /// compare against the one the panel standing was built from.
    /// </summary>
    internal static string Of(NodeEditor editor)
    {
        // A group's panel is its edge, and the edge moves whenever a wire is
        // drawn across it — which, like patching a module's input, is not a
        // selection change and would otherwise leave a stale list on screen.
        // Whether it is open is in here for the same reason: the button that
        // opens it says which way it goes.
        if (editor.SelectedGroup is { } group)
        {
            var sockets = editor.Patch.SocketsOf(group);

            // The name is in here as well, because the panel does not only show
            // it: the button that keeps a group in the module list is offered on
            // the strength of it, and is refused to a group with none. So a
            // rename has to rebuild this panel and not only the title in it.
            // Whether it is off, for the reason the module panel keeps it below:
            // switching is not a selection change, and the button says which way
            // it goes.
            var shape = new StringBuilder(
                $"g{group.Id:N}{(group.Collapsed ? 'c' : 'o')}"
                + $"{(editor.SelectionIsOff ? '-' : '+')}{group.Name}");

            // Whether each is wired as well as which they are: a socket keeps its
            // row when the wire comes off, but it grows the button that takes it
            // off the edge — so unplugging changes the panel without changing
            // which sockets are on it.
            // And what each row shows, which is a module's row for the same socket.
            foreach (var socket in sockets.Inputs.Concat(sockets.Outputs))
                shape.Append(
                    $"{(socket.IsOutput ? 'o' : 'i')}{socket.Node:N}.{socket.Port}"
                    + $"{(editor.Patch.Wired(group, socket) ? '+' : '-')}"
                    + (socket.IsOutput
                        ? $"{WireEnds.OutOf(editor.Patch, socket.Node, socket.Port)}|"
                        : Input(editor.Patch, editor.Patch.Find(socket.Node), socket.Port)));

            return shape.ToString();
        }

        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
            return string.Empty;

        var patched = new StringBuilder();

        for (var i = 0; i < def.Inputs.Count; i++)
            patched.Append(Input(editor.Patch, node, i));

        // Each row names the far end of its wires, so a rewire or a rename there
        // changes the panel.
        for (var i = 0; i < def.Outputs.Count; i++)
            patched.Append($"{WireEnds.OutOf(editor.Patch, node.Id, i)}|");

        // A knob's name and range are drawn on its row, so renaming it or changing
        // the range from elsewhere has to rebuild the panel.
        var linked = string.Concat(ControlMap.All(node).Select(l =>
            $"{l.Port}{editor.Patch.Control(l.Link.Control)?.Name}{l.Link.Min}{l.Link.Max}"));

        // Which groups the selection touches and which way round each is drawn,
        // because that is what the open and close buttons are offered on.
        var groups = new StringBuilder();

        foreach (var touched in editor.SelectedGroups)
            groups.Append($"{touched.Id:N}{(touched.Collapsed ? 'c' : 'o')}");

        // Switching a module off is not a selection change either, and the button
        // that does it says which way it goes.
        var switched = editor.SelectionIsOff ? '-' : '+';

        // An Auto remap's rows read the ranges at the far ends of its wires, which
        // move when its output is patched or the module feeding it is turned.
        var spans = def.TypeId == NodeCatalog.AutoRemapTypeId ? AutoRemap.Of(editor.Patch, node).ToString() : "";

        return $"{node.Id:N}{patched}{groups}{linked}{switched}{spans}";
    }

    /// <summary>Whether an input's row is a wire, a panel knob or a slider, and what the row names.</summary>
    private static string Input(Patch patch, NodeInstance? node, int port) =>
        node is null ? ""
        : WireEnds.Into(patch, node.Id, port) is { } from ? $"{from}|"
        : ControlMap.Of(node, port) is { } link && patch.Control(link.Control) is { } knob ? $"k{knob.Name}{link.Min}{link.Max}|"
        : ".";
}
