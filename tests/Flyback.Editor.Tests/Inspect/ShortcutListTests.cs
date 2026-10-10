using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Flyback.Core.Graph;
using Flyback.Editor.Inspect;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Inspect;

/// <summary>
/// With nothing selected the panel lists the canvas's gestures in groups that
/// fold, and which are open is remembered while a module is looked at.
/// </summary>
public class ShortcutListTests : EditorTest
{
    private (MainWindow Window, Guid Sine) Open()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var osc = b.Add(NodeCatalog.SineTypeId, 360, 40);
        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Wire(osc, 0, screen, NodeCatalog.OutputColorPort);

        return (Open(b.Patch), osc.Id);
    }

    private static Button Heading(MainWindow window, string group) =>
        All<Button>(window).Single(b => b.Name == ShortcutList.HeadingPrefix + group);

    private static bool IsOpen(MainWindow window, string group) =>
        Heading(window, group).Parent is Panel { Children: [_, { IsVisible: true }] };

    private static void Press(MainWindow window, string group)
    {
        Heading(window, group).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    [AvaloniaFact]
    public void Getting_started_is_open_and_the_rest_are_folded()
    {
        var (window, _) = Open();

        IsOpen(window, "Getting started").ShouldBeTrue();
        IsOpen(window, "Patching").ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_group_left_open_is_still_open_after_a_module_is_looked_at()
    {
        var (window, sine) = Open();

        Press(window, "Patching");
        Editor(window).Selection.Select(sine);
        Settle(window);
        Editor(window).Selection.Select(null);
        Settle(window);

        IsOpen(window, "Patching").ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Keys_are_drawn_one_cap_to_a_key()
    {
        var (window, _) = Open();

        All<Border>(window).Count(b => b.Name == ShortcutList.KeyName).ShouldBeGreaterThanOrEqualTo(5);
    }

    [AvaloniaFact]
    public void A_finger_has_no_keys_to_name_and_a_page_has_no_files()
    {
        InspectorHelp.Shortcuts(inPage: false, fingers: true).SelectMany(g => g.Rows).ShouldAllBe(r => r.Keys.Length == 0);

        InspectorHelp.Shortcuts(inPage: true, fingers: false)
            .SelectMany(g => g.Rows)
            .Select(r => r.Title)
            .ShouldNotContain("Save");
    }

    [AvaloniaFact]
    public void Drag_to_pan_is_what_the_help_names()
    {
        string Keys(bool dragToPan, string title) =>
            InspectorHelp.Shortcuts(inPage: false, fingers: false, dragToPan).SelectMany(g => g.Rows).Single(r => r.Title == title).Keys;

        Keys(dragToPan: false, "Pan").ShouldBe("Middle-drag");
        Keys(dragToPan: false, "Select").ShouldBe("Drag");
        Keys(dragToPan: true, "Pan").ShouldBe("Drag");
        Keys(dragToPan: true, "Select").ShouldBe("Right-drag");

        InspectorHelp.Locked(fingers: false, dragToPan: true).ShouldNotContain("middle");
    }
}
