using System.Text;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>What the inspector's rows are, as against what is in them, as one string to compare.</summary>
internal static class InspectorShape
{
    /// <summary>
    /// The selected module and which of its inputs are patched, as one string to
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
            foreach (var socket in sockets.Inputs.Concat(sockets.Outputs))
                shape.Append(
                    $"{(socket.IsOutput ? 'o' : 'i')}{socket.Node:N}.{socket.Port}"
                    + $"{(editor.Patch.Wired(group, socket) ? '+' : '-')}");

            return shape.ToString();
        }

        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
            return string.Empty;

        var patched = new char[def.Inputs.Count];

        for (var i = 0; i < patched.Length; i++)
            patched[i] = editor.Patch.IncomingTo(node.Id, i) is not null ? 'w'
                : ControlMap.Of(node, i) is { } link && editor.Patch.Control(link.Control) is not null ? 'k'
                : '.';

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

        return $"{node.Id:N}{new string(patched)}{groups}{linked}{switched}{spans}";
    }
}
