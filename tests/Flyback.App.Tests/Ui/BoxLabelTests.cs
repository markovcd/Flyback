using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// What a shut box and a module's header say when what they name is a formula:
/// one line each, cut where it is too long, and a box's socket named for what
/// feeds it.
/// </summary>
public class BoxLabelTests : UiTest
{
    private const string Long = "(1 - smoothstep(0.019, 0.021, abs(b - 0.925))) * step(a, (c + d * (1 / 13)) * 2.8 - 1.4)";

    private static NodeInstance Expression(PatchBuilder b, string formula, double x)
    {
        var node = b.Add(NodeCatalog.ExpressionTypeId, x, 0);
        node.SetState("expression", new JsonObject { ["formula"] = formula });
        return node;
    }

    /// <summary>
    /// A formula has spaces to break at, and wrapped it ran down over the rows
    /// below: a label cut short is still one line high.
    /// </summary>
    [AvaloniaFact]
    public void A_long_title_is_drawn_on_one_line()
    {
        var one = NodeEditor.Text("a", 12.5, Brushes.White, 180, true);
        var long_ = NodeEditor.Text(Long, 12.5, Brushes.White, 180, true);

        long_.Height.ShouldBe(one.Height, 0.5);
        long_.Width.ShouldBeLessThanOrEqualTo(180);
    }

    /// <summary>The port is what tells a box's sockets apart, so the middle goes and it stays.</summary>
    [AvaloniaFact]
    public void A_long_socket_label_keeps_its_port()
    {
        var fitted = NodeEditor.Fit(Long + ".out", 170);

        fitted.ShouldEndWith("….out");
        fitted.ShouldStartWith("(1 - smoothstep");
        NodeEditor.Text(fitted, 11.5, Brushes.White, 1000, false).Width.ShouldBeLessThanOrEqualTo(170);

        NodeEditor.Fit("filter.cutoff", 170).ShouldBe("filter.cutoff");
    }

    /// <summary>
    /// An Expression's input on a box's edge is named for what is wired into it,
    /// which says what the socket carries where the formula would say it once a socket.
    /// </summary>
    [AvaloniaFact]
    public void A_box_names_an_expressions_input_for_what_feeds_it()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var clock = b.Add(NodeCatalog.TimeTypeId, 0, 0);
        var inner = Expression(b, Long, 300);
        var tail = Expression(b, "a * 2", 600);
        var sink = b.Add(NodeCatalog.OutputTypeId, 900, 0);
        b.Wire(clock, 0, inner, 0).Wire(inner, 0, tail, 0).Wire(tail, 0, sink, NodeCatalog.OutputLeftPort);

        var group = b.Patch.Group([inner.Id, tail.Id]).ShouldNotBeNull();
        group.Collapsed = true;

        var editor = new NodeEditor { Width = 1200, Height = 800 };
        var window = Show(editor, 1200);
        editor.Patch = b.Patch;
        Settle(window);

        var sockets = b.Patch.SocketsOf(group);

        editor.Named(sockets.Inputs.ShouldHaveSingleItem()).ShouldNotBeNull().Label.ShouldBe("Time.t");
        editor.Named(sockets.Outputs.ShouldHaveSingleItem()).ShouldNotBeNull().Label.ShouldBe("a * 2.out");
    }

    /// <summary>
    /// A socket is its letter standing alone: the a in abs and the c in fract are
    /// not sockets, and an Expression shows a knob only for a socket it reads.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("sin(a * 6 + b) * c + 0.5", 0, true)]
    [InlineData("sin(a * 6 + b) * c + 0.5", 3, false)]
    [InlineData("abs(b) * 2", 0, false)]
    [InlineData("abs(b) * 2", 1, true)]
    [InlineData("fract(a) + tau", 2, false)]
    [InlineData("a*b", 1, true)]
    public void A_formula_reads_the_sockets_it_names(string formula, int socket, bool reads) =>
        NodeEditor.Reads(formula, socket).ShouldBe(reads);
}
