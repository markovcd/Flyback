using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// That what a module listing tells a model to call is a tool the workbench
/// actually offers.
/// </summary>
/// <remarks>
/// The one thing nothing checked. A tool name is written out in four
/// places — declared, dispatched, explained in the handbook's preamble, and
/// named again inside the engine's <see cref="NodeExtra.Announce"/>, which is in
/// an assembly that cannot see any of the other three. Renaming a tool would
/// have left every module telling a model to call the old name, with the build
/// clean and every test green; and the drift had already happened in the
/// quieter direction, the preamble having gained lines for two of the four kinds
/// and never the others.
/// <para>
/// So this is not a test of <see cref="Vocabulary"/>'s arithmetic — that is a
/// switch, and a switch that is wrong is wrong obviously. It is a test that the
/// two lists have not come apart, which is the failure that is otherwise silent.
/// </para>
/// </remarks>
public class VocabularyTests
{
    private static readonly IReadOnlyList<string> Offered =
        [.. new PatchWorkbench(NodeCatalog.BuiltIn, new Patch()).Tools.Select(t => t.Name)];

    /// <summary>
    /// Every kind in the built-in catalogue, which is every kind the engine
    /// ships and every kind a preset can use.
    /// </summary>
    private static IEnumerable<NodeExtra> Carried =>
        NodeCatalog.BuiltIn.All
            .SelectMany(def => def.Extras)
            .DistinctBy(extra => extra.Key);

    [Fact]
    public void Every_kind_of_carried_state_names_a_tool_that_exists()
    {
        Carried.ShouldNotBeEmpty("the catalogue should carry something, or this checks nothing");

        foreach (var extra in Carried)
            Offered.ShouldContain(
                Vocabulary.ToolFor(extra.Key),
                $"'{extra.Key}' is announced as set by a tool the workbench does not offer");
    }

    /// <summary>
    /// And that the line a model reads actually carries the name, rather than
    /// the two halves being joined somewhere a listing never goes through.
    /// </summary>
    [Fact]
    public void The_announcement_says_which_tool_writes_it()
    {
        foreach (var extra in Carried)
            Vocabulary.Announce(extra).ShouldContain(Vocabulary.ToolFor(extra.Key));
    }

    /// <summary>
    /// A kind the engine has never heard of is <c>set_extra</c>'s, which is what
    /// makes the fallback right by construction rather than by luck — a plugin
    /// declares fields and that tool writes them, whatever the kind is called.
    /// </summary>
    [Fact]
    public void A_kind_nobody_wrote_a_tool_for_is_set_with_set_extra()
    {
        Vocabulary.ToolFor("glide").ShouldBe(Vocabulary.SetExtra);
        Offered.ShouldContain(Vocabulary.SetExtra);
    }

    /// <summary>
    /// And a kind sent to <c>set_extra</c> has to be one it can actually write.
    /// </summary>
    /// <remarks>
    /// The sharp edge of the fallback, and the reason it is worth a test of its
    /// own. That tool writes <see cref="NodeExtra.Fields"/> and refuses a kind
    /// that declares none, so a new engine kind — no fields, because the engine's
    /// four have none, and no tool, because nobody wrote one — would be announced
    /// as settable with <c>set_extra</c> and refused by it. The listing would be
    /// telling a model to make a call that cannot succeed.
    /// </remarks>
    [Fact]
    public void A_kind_sent_to_set_extra_is_one_set_extra_can_write()
    {
        foreach (var extra in Carried.Where(e => Vocabulary.ToolFor(e.Key) == Vocabulary.SetExtra))
            extra.Fields.ShouldNotBeEmpty(
                $"'{extra.Key}' has no tool of its own and no fields, so nothing can set it");
    }

    /// <summary>
    /// The half the engine says stays the engine's. It describes what the module
    /// carries and names no tool, because the assembly it is in cannot see one.
    /// </summary>
    [Fact]
    public void The_engine_half_of_the_line_names_no_tool()
    {
        foreach (var extra in Carried)
            extra.Announce()
                .Contains("set_", StringComparison.Ordinal)
                .ShouldBeFalse($"'{extra.Key}' names a tool the engine cannot see");
    }
}
