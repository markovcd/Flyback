using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Pointing from a source file at the modules it makes, and back at the numbers
/// it writes for their knobs.
/// </summary>
/// <remarks>
/// By position rather than by name, which is the whole of why this exists. The
/// module in <c>atan2(a: 1.5) |&gt; out.left</c> is called nothing, and anything
/// that needed a name to find it would have to invent one and write it into
/// somebody's file.
/// </remarks>
public class SourceMapTests
{
    public static TheoryData<string> Names => [.. Presets.All.Select(p => p.Name)];

    private static Patch Preset(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn);

    private static LanguageLoad Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    /// <summary>The module the caret is in, where the caret is just after a word.</summary>
    private static NodeInstance? Under(LanguageLoad load, string source, string word) =>
        load.Map.At(source.IndexOf(word, StringComparison.Ordinal) + 1) is { } id
            ? load.Patch.Find(id)
            : null;

    /// <summary>The socket a name stands for, since a knob is keyed by its index.</summary>
    private static int Port(NodeInstance node, string name)
    {
        var inputs = NodeCatalog.BuiltIn.Require(node.TypeId).Inputs;

        for (var i = 0; i < inputs.Count; i++)
            if (string.Equals(inputs[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;

        throw new ArgumentException($"'{node.TypeId}' has no socket called '{name}'.", nameof(name));
    }

    [Fact]
    public void A_call_with_no_name_is_still_something_to_point_at()
    {
        const string source = "atan2(a: 1.5524476) |> out.left";

        var load = Built(source);

        Under(load, source, "atan2")!.TypeId.ShouldBe("math.atan2");
    }

    [Fact]
    public void A_knob_is_changed_where_the_text_already_says_it()
    {
        const string source = "atan2(a: 1.5524476) |> out.left";

        var load = Built(source);
        var node = Under(load, source, "atan2")!;

        var change = load.Map.Knob(node.Id, 0, "a", "2");

        change.ShouldNotBeNull();
        Applied(source, change.Value).ShouldBe("atan2(a: 2) |> out.left");
    }

    /// <summary>
    /// A knob at its default is written nowhere, so there is no number to
    /// replace and it has to be added to the call that placed the module.
    /// </summary>
    [Fact]
    public void A_knob_the_text_does_not_mention_is_added_to_the_call()
    {
        const string source = "atan2(a: 1.5) |> out.left";

        var load = Built(source);
        var node = Under(load, source, "atan2")!;

        Applied(source, load.Map.Knob(node.Id, 1, "b", "0.25")!.Value)
            .ShouldBe("atan2(a: 1.5, b: 0.25) |> out.left");
    }

    [Fact]
    public void A_knob_added_to_an_empty_call_takes_no_comma()
    {
        const string source = "atan2() |> out.left";

        var load = Built(source);
        var node = Under(load, source, "atan2")!;

        Applied(source, load.Map.Knob(node.Id, 0, "a", "2")!.Value).ShouldBe("atan2(a: 2) |> out.left");
    }

    /// <summary>
    /// The sign is part of the number. Putting 0.5 where the digits of -0.5 are
    /// would leave the file saying -0.5 still.
    /// </summary>
    [Fact]
    public void A_negative_knob_is_replaced_with_its_minus()
    {
        const string source = "atan2(a: -0.5) |> out.left";

        var load = Built(source);
        var node = Under(load, source, "atan2")!;

        Applied(source, load.Map.Knob(node.Id, 0, "a", "0.5")!.Value).ShouldBe("atan2(a: 0.5) |> out.left");
    }

    /// <summary>
    /// A knob worked out from two numbers has no single figure in the file to
    /// put another one in place of, and adding a second would leave the file
    /// asserting two different values for one socket.
    /// </summary>
    [Fact]
    public void A_knob_written_as_arithmetic_is_left_alone()
    {
        const string source = "atan2(a: 1 / 12) |> out.left";

        var load = Built(source);
        var node = Under(load, source, "atan2")!;

        load.Map.Knob(node.Id, 0, "a", "2").ShouldBeNull();
    }

    /// <summary>
    /// A knob said by the statement the language has for saying it is changed
    /// there, rather than added to the call a few lines up.
    /// </summary>
    [Fact]
    public void A_knob_set_by_a_statement_is_changed_in_that_statement()
    {
        const string source = "let hum = sine()\nhum.freq = 220\nhum |> out.left";

        var load = Built(source);
        var node = load.Patch.Nodes.Single(n => n.Name == "hum");

        Applied(source, load.Map.Knob(node.Id, Port(node, "freq"), "freq", "440")!.Value)
            .ShouldBe("let hum = sine()\nhum.freq = 440\nhum |> out.left");
    }

    /// <summary>
    /// The caret inside an argument is in the module that argument is, not in
    /// the one it is being handed to.
    /// </summary>
    [Fact]
    public void The_innermost_call_wins()
    {
        const string source = "mul(a: sine(), b: saw()) |> out.left";

        var load = Built(source);

        Under(load, source, "sine")!.TypeId.ShouldBe("osc.sine");
        Under(load, source, "saw")!.TypeId.ShouldBe("osc.saw");
        Under(load, source, "mul")!.TypeId.ShouldBe("math.mul");
    }

    /// <summary>
    /// The name a binding gives is the last stage of its pipeline, so clicking
    /// it points at the module it names rather than at the first one on the line.
    /// </summary>
    [Fact]
    public void A_bindings_name_points_at_the_module_it_names()
    {
        const string source = "let hum = sine(freq: 220) |> math.mul(b: 0.5)\nhum |> out.left";

        var load = Built(source);

        Under(load, source, "let hum")!.TypeId.ShouldBe("math.mul");
        Under(load, source, "sine")!.TypeId.ShouldBe("osc.sine");
    }

    /// <summary>
    /// A printing is text nobody wrote, and it has to be as clickable as text
    /// somebody did — that is what the code view of a patch built on the canvas
    /// is showing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void A_printing_points_at_every_module_it_writes(string name)
    {
        var patch = Preset(name);
        var printing = PatchPrinter.Written(patch, NodeCatalog.BuiltIn);

        // Every module except the shared Coordinates and Time, which are written
        // as the bare words the language has for them and place nothing.
        var expected = patch.Nodes
            .Where(node => node.TypeId != NodeCatalog.CoordTypeId && node.TypeId != NodeCatalog.TimeTypeId)
            .Where(node => !NodeCatalog.IsSink(node.TypeId))
            .Select(node => node.Id);

        foreach (var id in expected)
            printing.Map.Where(id).ShouldNotBeNull($"{name}: nothing in the printing points at {id}");
    }

    /// <summary>
    /// A knob turned on a printed patch goes back into the number the printing
    /// wrote for it, so the reading stays a true one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Every_knob_a_printing_writes_can_be_changed_where_it_stands(string name)
    {
        var patch = Preset(name);
        var printing = PatchPrinter.Written(patch, NodeCatalog.BuiltIn);

        foreach (var node in patch.Nodes)
        {
            if (NodeCatalog.BuiltIn.Get(node.TypeId) is not { } def) continue;

            for (var port = 0; port < def.Inputs.Count && port < node.InputValues.Length; port++)
            {
                if (patch.IncomingTo(node.Id, port) is not null) continue;
                if (NodeCatalog.BuiltIn.Normalled(def.Inputs[port]) is not null) continue;
                if (printing.Map.Where(node.Id) is null) continue;

                var socket = def.Inputs[port].Name.Replace(' ', '_');

                // Spelled the way a printing spells one, since a note and a
                // length of time are not the number the socket holds.
                var value = PatchPrinter.Knob(0.125f, def.Inputs[port].Display);
                var change = printing.Map.Knob(node.Id, port, socket, value);

                change.ShouldNotBeNull($"{name}: '{socket}' on {node.TypeId} has nowhere to be written");

                var rewritten = PatchLanguage.Build(Applied(printing.Source, change.Value), NodeCatalog.BuiltIn);

                rewritten.Issues.ShouldBeEmpty($"{name}: {rewritten.Report}");
            }
        }
    }

    private static string Applied(string source, Change change) =>
        source[..change.Offset] + change.Text + source[(change.Offset + change.Length)..];
}
