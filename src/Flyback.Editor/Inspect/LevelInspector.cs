using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Canvas;
using Flyback.Editor.Hubs;
using Flyback.Engine.Language;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// What the inspector shows for a level the text opened, once its modules are the
/// selection: what it was called with, what it holds, the wires leaving it and the
/// other calls of its def. Read only, since the text owns the patch.
/// </summary>
internal sealed class LevelInspector(NodeEditor editor, InspectorSurface surface, PatchLevels levels, Document document)
{
    public const string ShowInTextName = "show-in-text";

    public void Build(Level level, bool fingers)
    {
        var path = levels.PathTo(level);
        var at = PassAt(path);
        var pass = at >= 0 ? path[at] : null;
        var loop = at > 0 && path[at - 1].Kind == LevelKind.Loop ? path[at - 1] : null;
        var plate = ModulePlate.Box();

        surface.Wash.ShowBox();
        surface.Wash.Off = false;

        plate.Named.Children.Add(new TextBlock
        {
            Name = "level-title",
            Text = Title(level),
            FontSize = Text.Heading,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        plate.Named.Children.Add(new TextBlock
        {
            Text = pass is { } made && !ReferenceEquals(made, level)
                ? $"{Kind(level.Kind)} · pass {made.Pass}/{made.Passes} · line {level.Line}"
                : $"{Kind(level.Kind)} · line {level.Line}",
            FontSize = Text.Small,
            Foreground = plate.Quiet,
        });

        plate.Face = new PlateFace(Title(level), Kind(level.Kind), ModuleGlyphs.Group, new SolidColorBrush(Colors.Label), null, null);
        surface.PlateHost.Content = plate;

        if (path.Count > 1) surface.Panel.Children.Add(LevelPath.Row(editor, levels, path, null, fingers));

        var given = pass?.Item is { } item && loop?.Name is { } names
            ? [$"{names["for ".Length..]} = {item}"]
            : level.Arguments.Select(argument => argument.ToString()).ToList();

        Section("Takes", given.Select(Line));

        var own = levels.Present(level with { Inside = [] }).Count;
        var inside = level.Inside.Select((inner, i) => (Control)LevelPath.Button(editor, levels, inner, $"inside-{i}", null, fingers)).ToList();

        if (own > 0) inside.Add(Line(own == 1 ? "1 module of its own" : $"{own} modules of its own"));

        Section("Inside", inside);

        Section("Leaving", level.Leaving(editor.History.Patch).Select(wire => Line(
            $"{WireEnds.Name(editor.History.Patch, wire.SourceNode, wire.SourcePort, output: true)} → {WireEnds.Name(editor.History.Patch, wire.TargetNode, wire.TargetPort, output: false)}")));

        if (level.Kind == LevelKind.Call && level.Name is { } def)
            Section($"Other calls of {def}", levels.CallsOf(def)
                .Where(call => !ReferenceEquals(call, level))
                .Select((call, i) => (Control)LevelPath.Button(editor, levels, call, $"call-{i}", null, fingers, Title(call))));

        var show = new Button
        {
            Name = ShowInTextName,
            Content = "Show in text",
            FontSize = Text.Small,
            MinHeight = fingers ? 44 : 24,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        ToolTip.SetTip(show, $"Open the text at line {level.Line}, where this level is written");
        show.Click += (_, _) => document.ShowLine(level.Line, level.Column);

        surface.Panel.Children.Add(show);
    }

    /// <summary>Where in <paramref name="path"/> the loop pass is that it ends in, or that made its last level and nothing else; -1 for none.</summary>
    private static int PassAt(IReadOnlyList<Level> path)
    {
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (path[i].Kind == LevelKind.Pass) return i;
            if (i < path.Count - 1 && (path[i].Members.Count > 0 || path[i].Inside.Count > 1)) return -1;
        }

        return -1;
    }

    /// <summary>The level as its chip reads, with a call's arguments: <c>bass = voice(55, 3)</c>.</summary>
    private static string Title(Level level) =>
        level.Kind == LevelKind.Call && level.Arguments.Count > 0
            ? $"{level.Label}({string.Join(", ", level.Arguments.Select(argument => argument.Value))})"
            : level.Label;

    private static string Kind(LevelKind kind) => kind switch
    {
        LevelKind.Group => "Group",
        LevelKind.Call => "Def call",
        LevelKind.Loop => "Loop",
        _ => "Loop pass",
    };

    private void Section(string heading, IEnumerable<Control> rows)
    {
        var listed = rows.ToList();

        if (listed.Count == 0) return;

        surface.Panel.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = Text.Small,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.7,
            Margin = new Thickness(0, 10, 0, 2),
        });

        foreach (var row in listed) surface.Panel.Children.Add(row);
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        FontSize = Text.Body,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(6, 1, 0, 1),
    };
}
