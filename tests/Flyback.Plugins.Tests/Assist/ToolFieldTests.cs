using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

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
            ToolFields.WireFrom,
            ToolFields.WireFromPort,
            ToolFields.Knobs,
        ]))!.AsObject();

        var properties = schema["properties"]!.AsObject();

        properties.Select(p => p.Key).ShouldBe(["from", "from_port", "knobs"]);
        properties["from"]!["type"]!.GetValue<string>().ShouldBe("string");
        properties["from"]!["description"]!.GetValue<string>().ShouldBe(ToolFields.WireFrom.Description);
        properties["knobs"]!["items"]!["properties"]!["port"]!["type"]!.GetValue<string>().ShouldBe("string");
        properties["knobs"]!["items"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["port", "value"]);
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["from", "knobs"]);
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
        var sent = Arguments("""{ "handle": "sine1", "off": false, "tonic": 9, "seconds": "two", "path": "" }""");

        ToolFields.Handle.Text(sent, out var handle).ShouldBeTrue();
        handle.ShouldBe("sine1");
        ToolFields.Off.Flag(sent, fallback: true).ShouldBeFalse();
        ToolFields.StartsSilent.Flag(sent, fallback: true).ShouldBeTrue();
        ToolFields.Tonic.Whole(sent, out var tonic).ShouldBeTrue();
        tonic.ShouldBe(9);
        ToolFields.Seconds.Number(sent, out _).ShouldBeFalse();
        ToolFields.SamplePath.Text(sent, out _).ShouldBeFalse();
        ToolFields.Handle.Text(Arguments("[]"), out _).ShouldBeFalse();
    }
}
