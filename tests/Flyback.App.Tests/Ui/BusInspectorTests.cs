using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// A Send's bus typed over in its panel: the Receives on it go where it goes, as one edit.
/// </summary>
public class BusInspectorTests : UiTest
{
    private MainWindow Open(out NodeInstance send, out NodeInstance receive)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var level = b.Add("value", 40, 40, (0, 0.3f));
        var sent = b.Add(NodeCatalog.SendTypeId, 360, 40);
        var heard = b.Add(NodeCatalog.ReceiveTypeId, 360, 300);
        var screen = b.Add(NodeCatalog.OutputTypeId, 700, 300);

        sent.SetState("bus", new JsonObject { ["bus"] = "kick" });
        heard.SetState("bus", new JsonObject { ["bus"] = "kick" });

        b.Wire(level, 0, sent, 0).Wire(heard, 0, screen, NodeCatalog.OutputLeftPort);

        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Editor(window).History.Open(b.Patch);
        Settle(window);

        send = Editor(window).History.Patch.Find(sent.Id)!;
        receive = Editor(window).History.Patch.Find(heard.Id)!;

        return window;
    }

    private static void Select(MainWindow window, NodeInstance node)
    {
        var editor = Editor(window);
        var header = new Point(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(header), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    private static TextBox Bus(MainWindow window) =>
        All<TextBox>(window).Single(t => t.MaxLength == ExtraField.Text.Limit);

    private static void Keep(MainWindow window, string bus)
    {
        Bus(window).Text = bus;
        Bus(window).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Settle(window);
    }

    [AvaloniaFact]
    public void A_Send_put_on_another_bus_takes_its_Receive_and_one_undo_puts_both_back()
    {
        var window = Open(out var send, out var receive);

        Select(window, send);
        Keep(window, "thump");

        NodeCatalog.BusOf(send).ShouldBe("thump");
        NodeCatalog.BusOf(receive).ShouldBe("thump");

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Settle(window);

        var patch = Editor(window).History.Patch;

        NodeCatalog.BusOf(patch.Find(send.Id)!).ShouldBe("kick");
        NodeCatalog.BusOf(patch.Find(receive.Id)!).ShouldBe("kick");
    }

    [AvaloniaFact]
    public void A_Receive_put_on_another_bus_goes_alone()
    {
        var window = Open(out var send, out var receive);

        Select(window, receive);
        Keep(window, "snare");

        NodeCatalog.BusOf(receive).ShouldBe("snare");
        NodeCatalog.BusOf(send).ShouldBe("kick");
    }
}
