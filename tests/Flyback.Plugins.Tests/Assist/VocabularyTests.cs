using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

/// <summary>
/// That what a module listing tells a model about writing what a module carries is
/// something the text can say.
/// </summary>
public class VocabularyTests
{
    /// <summary>
    /// Every kind in the built-in catalog, which is every kind the engine
    /// ships and every kind a preset can use.
    /// </summary>
    private static IEnumerable<NodeExtra> Carried =>
        NodeCatalog.BuiltIn.All
            .SelectMany(def => def.Extras)
            .DistinctBy(extra => extra.Key);

    [Fact]
    public void The_announcement_says_how_the_text_writes_it()
    {
        Carried.ShouldNotBeEmpty("the catalog should carry something, or this checks nothing");

        foreach (var extra in Carried)
            Vocabulary.Announce(extra).ShouldEndWith($", written as {Vocabulary.WrittenAs(extra)}");
    }

    /// <summary>
    /// A kind the engine has never heard of is named arguments, which is right by
    /// construction: a plugin declares fields and the text names them.
    /// </summary>
    [Fact]
    public void A_kind_nobody_wrote_a_notation_for_is_named_arguments() =>
        Vocabulary.WrittenAs(new Glide()).ShouldBe("named arguments");

    /// <summary>
    /// And a kind announced as named arguments has to declare some, or the text has
    /// no way to write it.
    /// </summary>
    [Fact]
    public void A_kind_written_as_named_arguments_declares_fields()
    {
        foreach (var extra in Carried.Where(e => Vocabulary.WrittenAs(e) == "named arguments"))
            extra.Fields.ShouldNotBeEmpty(
                $"'{extra.Key}' has no notation of its own and no fields, so the text cannot write it");
    }

    /// <summary>
    /// The half the engine says stays the engine's, naming no tool, because the
    /// assembly it is in cannot see one.
    /// </summary>
    [Fact]
    public void The_engine_half_of_the_line_names_no_tool()
    {
        foreach (var extra in Carried)
            extra.Announce()
                .Contains("set_", StringComparison.Ordinal)
                .ShouldBeFalse($"'{extra.Key}' names a tool the engine cannot see");
    }

    /// <summary>A plugin's file is the call's string, as the engine's are.</summary>
    [Fact]
    public void A_file_a_plugin_reads_is_written_as_the_calls_string()
    {
        var drawing = ShippedPlugins.Loaded.Modules.Require("flyback.drawings.path").Extras.ShouldHaveSingleItem();

        Vocabulary.WrittenAs(drawing).ShouldBe("the call's one unnamed string");
    }

    /// <summary>A kind no notation was written for, as a plugin's might be.</summary>
    private sealed record Glide : NodeExtra
    {
        public override string Key => "glide";
    }
}
