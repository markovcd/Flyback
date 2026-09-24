using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The panel heads a module with its description, gives each socket its help as
/// the tip on its row, and lists the outputs after everything that can be set.
/// </summary>
public class SocketHelpInspectorTests : UiTest
{
    private static readonly NodeDef Filter = NodeCatalog.BuiltIn.Require(NodeCatalog.FilterTypeId);

    private MainWindow Selected(string typeId)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var module = b.Add(typeId, 360, 40);

        if (typeId != NodeCatalog.OutputTypeId) b.Wire(module, 0, output, NodeCatalog.OutputLeftPort);

        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var editor = All<NodeEditor>(window).Single();

        editor.History.Open(b.Patch);
        Settle(window);

        editor.Selection.Select(module.Id);
        Settle(window);

        return window;
    }

    /// <summary>The row a caption stands on, which is what carries the socket's tip.</summary>
    private static Control Row(MainWindow window, string caption, int nth = 0) =>
        All<TextBlock>(window).Where(t => t.Text == caption).ElementAt(nth).Parent as Control
        ?? throw new InvalidOperationException($"'{caption}' stands on nothing");

    [AvaloniaFact]
    public void The_description_heads_the_panel()
    {
        var window = Selected(NodeCatalog.FilterTypeId);

        All<TextBlock>(window).ShouldContain(t => t.Text == Filter.Description);
    }

    [AvaloniaFact]
    public void An_input_row_carries_its_sockets_help_as_its_tip()
    {
        var window = Selected(NodeCatalog.FilterTypeId);

        var cutoff = Filter.Inputs.Single(port => port.Name == "cutoff");

        cutoff.Help.ShouldNotBeEmpty();
        ToolTip.GetTip(Row(window, "cutoff")).ShouldBe(cutoff.Help);
    }

    [AvaloniaFact]
    public void The_outputs_are_listed_last_each_with_its_help()
    {
        var window = Selected(NodeCatalog.FilterTypeId);

        var text = All<TextBlock>(window).Select(t => t.Text).ToList();
        var heading = text.IndexOf("Outputs");

        heading.ShouldBeGreaterThan(text.IndexOf("resonance"));

        foreach (var port in Filter.Outputs)
        {
            text.IndexOf(port.Name, heading).ShouldBeGreaterThan(heading);
            ToolTip.GetTip(Row(window, port.Name)).ShouldBe(port.Help);
        }
    }

    /// <summary>A socket that takes a standard help shows the standard's words.</summary>
    [AvaloniaFact]
    public void A_standard_socket_shows_the_standard_help()
    {
        var window = Selected(NodeCatalog.SineTypeId);

        var freq = NodeCatalog.BuiltIn.Require(NodeCatalog.SineTypeId).Inputs.Single(port => port.Name == "freq");

        freq.Standard.ShouldBeTrue();
        ToolTip.GetTip(Row(window, "freq")).ShouldBe(SocketHelp.Inputs["freq"]);
    }

    /// <summary>A setting carried on the node has its help as the tip on its row, as a socket does.</summary>
    [AvaloniaFact]
    public void A_settings_row_carries_its_help_as_its_tip()
    {
        var window = Selected(NodeCatalog.ExpressionTypeId);

        var formula = NodeCatalog.BuiltIn.Require(NodeCatalog.ExpressionTypeId).Extras.Single().Fields.Single();
        var caption = All<TextBlock>(window).Single(t => t.Text == formula.Label);

        formula.Help.ShouldNotBeEmpty();
        caption.GetSelfAndVisualAncestors().OfType<Control>().Select(ToolTip.GetTip).ShouldContain(formula.Help);
    }

    /// <summary>The Output puts nothing out, so it gets no heading for it.</summary>
    [AvaloniaFact]
    public void A_module_with_no_outputs_lists_none()
    {
        var window = Selected(NodeCatalog.OutputTypeId);

        All<TextBlock>(window).ShouldNotContain(t => t.Text == "Outputs");
    }
}
