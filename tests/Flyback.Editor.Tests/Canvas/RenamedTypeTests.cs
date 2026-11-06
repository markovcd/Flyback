using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>A renamed module still says what it is: after its name on the band, and on the inspector's kind line.</summary>
public class RenamedTypeTests : EditorTest
{
    private static NodeDef Sine => NodeCatalog.Get("osc.sine")!;

    private static NodeInstance Named(string? name)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var node = b.Add("osc.sine", 0, 0);
        node.Name = name;
        return node;
    }

    [AvaloniaFact]
    public void A_module_keeping_its_own_name_hides_no_type()
    {
        ModuleFace.HiddenType(Named(null), Sine).ShouldBeNull();
        ModuleFace.HiddenType(Named("sine"), Sine).ShouldBeNull("its own name in other letters still says what it is");
        ModuleFace.KindOf(Named(null), Sine).ShouldBe(Sine.Category);
    }

    [AvaloniaFact]
    public void A_renamed_module_names_its_type()
    {
        ModuleFace.HiddenType(Named("across"), Sine).ShouldBe("Sine");
        ModuleFace.KindOf(Named("across"), Sine).ShouldBe($"Sine · {Sine.Category}");
    }

    [AvaloniaFact]
    public void A_Send_named_after_its_bus_already_says_Send()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var send = b.Add(NodeCatalog.SendTypeId, 0, 0);
        send.SetState("bus", new JsonObject { ["bus"] = "kick" });
        var def = NodeCatalog.Get(NodeCatalog.SendTypeId)!;

        send.Name = "kick";
        ModuleFace.HiddenType(send, def).ShouldBeNull();
        ModuleFace.Heading(send, def).ShouldBe("Send · kick");

        send.Name = "thump";
        ModuleFace.HiddenType(send, def).ShouldBe("Send");
    }

    [AvaloniaFact]
    public void The_type_follows_the_name_on_its_line_and_gives_way_when_the_name_fills_the_band()
    {
        var bounds = new Rect(0, 0, NodeGeometry.Width, 120);
        var room = ModuleFace.HeaderWidth(bounds, tagged: false, marked: true);

        var title = CanvasText.Text("across", ModuleFace.HeaderSize, Brushes.White, room, true);
        var at = ModuleFace.TitleAt(bounds, title, marked: true);

        var laid = ModuleFace.LayType("Sine", bounds, title, at, room, Brushes.White).ShouldNotBeNull();

        laid.At.X.ShouldBeGreaterThan(at.X + title.Width, "after the name");
        (laid.At.X + laid.Text.Width).ShouldBeLessThanOrEqualTo(at.X + room + 0.5, "inside the band");
        (laid.At.Y + laid.Text.Baseline).ShouldBe(at.Y + title.Baseline, 0.5, "on the name's baseline");

        var full = CanvasText.Text(new string('m', 80), ModuleFace.HeaderSize, Brushes.White, room, true);

        ModuleFace.LayType("Sine", bounds, full, ModuleFace.TitleAt(bounds, full, marked: true), room, Brushes.White)
            .ShouldBeNull("the name keeps the band and the type is left out");
    }

    [AvaloniaFact]
    public void The_inspector_names_a_renamed_modules_type_and_drops_it_once_the_name_is_cleared()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var made = b.Add("osc.sine", 40, 40);
        made.Name = "across";

        var window = Open(b.Patch);
        var editor = Editor(window);
        var sine = editor.History.Patch.Find(made.Id)!;

        editor.Selection.Take([sine.Id]);
        editor.Selection.Announce();
        Settle(window);

        Kind(window).ShouldBe($"SINE · {Sine.Category.ToUpperInvariant()}");

        sine.Name = null;
        editor.History.Record();
        Settle(window);

        Kind(window).ShouldBe(Sine.Category.ToUpperInvariant());
    }

    private static string? Kind(Window window) => All<TextBlock>(window).Single(t => t.Name == "plate-kind").Text;
}
