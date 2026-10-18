using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

/// <summary>A tool's arguments, declared once and read by name through the same field.</summary>
public class ToolFieldTests
{
    private static readonly PatchWorkbench Bench = new(NodeCatalog.BuiltIn, new Patch(), vision: true, hearing: Listener.Itself);

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void A_schema_describes_each_field_and_lists_the_required_ones()
    {
        var schema = JsonNode.Parse(ToolField.Schema(
        [
            ToolFields.Source,
            ToolFields.Note,
            ToolFields.Edits,
        ]))!.AsObject();

        var properties = schema["properties"]!.AsObject();

        properties.Select(p => p.Key).ShouldBe(["source", "note", "edits"]);
        properties["source"]!["type"]!.GetValue<string>().ShouldBe("string");
        properties["source"]!["description"]!.GetValue<string>().ShouldBe(ToolFields.Source.Description);
        properties["edits"]!["items"]!["properties"]!["find"]!["type"]!.GetValue<string>().ShouldBe("string");
        properties["edits"]!["items"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["find", "replace"]);
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["source", "edits"]);
    }

    [Fact]
    public void A_tool_with_no_arguments_has_an_empty_schema() =>
        ToolField.Schema([]).ShouldBe("{}");

    [Fact]
    public void Every_tool_requires_only_what_it_describes()
    {
        foreach (var tool in Bench.Tools)
        {
            var schema = JsonNode.Parse(tool.Schema)!.AsObject();
            var described = schema["properties"]?.AsObject().Select(p => p.Key).ToHashSet() ?? [];
            var required = schema["required"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [];

            required.Where(name => !described.Contains(name)).ShouldBeEmpty(tool.Name);
        }
    }

    [Fact]
    public void A_field_reads_its_own_name_and_only_what_its_kind_holds()
    {
        var sent = Arguments("""{ "type_id": "osc.sine", "starts_silent": false, "from": 9, "seconds": "two", "summary": "" }""");

        ToolFields.TypeId.Text(sent, out var type).ShouldBeTrue();
        type.ShouldBe("osc.sine");
        ToolFields.StartsSilent.Flag(sent, fallback: true).ShouldBeFalse();
        ToolFields.StartsSilent.Flag(Arguments("{}"), fallback: true).ShouldBeTrue();
        ToolFields.From.Number(sent, out var from).ShouldBeTrue();
        from.ShouldBe(9);
        ToolFields.Seconds.Number(sent, out _).ShouldBeFalse();
        ToolFields.Summary.Text(sent, out _).ShouldBeFalse();
        ToolFields.TypeId.Text(Arguments("[]"), out _).ShouldBeFalse();
    }
}
