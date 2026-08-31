using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The palette's sections: that they are a set somebody chose, and that they come
/// out in the order somebody chose them in.
/// </summary>
/// <remarks>
/// Both were missing while a category was only a word a module wrote down, and
/// what went wrong was not something a reader of any one file could have caught.
/// Two providers meant different things by "Space" — the engine's Rotate and a
/// plugin's Reverb — so the palette drew a room and a rotation as one section.
/// Two meant nearly the same thing by different words, because "Shape" was taken
/// by the waveshapers and the shapes had to be called something else. And the
/// sections came out in registration order, so installing a plugin could move
/// them about.
/// </remarks>
public class ModuleCategoriesTests
{
    /// <summary>
    /// Every module the engine ships names a category the engine knows. This is
    /// the one that stops a stray string coming back: a new section is a decision
    /// to be made in one place rather than a word typed beside a module.
    /// </summary>
    [Fact]
    public void Every_module_the_engine_ships_names_a_category_it_knows()
    {
        foreach (var def in NodeCatalog.BuiltIn.All)
            ModuleCategories.All.ShouldContain(
                def.Category,
                $"'{def.Name}' is filed under '{def.Category}', which is not a section — "
                + $"add it to {nameof(ModuleCategories)} or file the module under one that is");
    }

    /// <summary>The sections come out in the declared order, not the load order.</summary>
    [Fact]
    public void The_sections_are_in_the_order_they_are_declared_in()
    {
        var shown = NodeCatalog.BuiltIn.Categories.ToList();

        shown.ShouldBe(ModuleCategories.All.Where(shown.Contains).ToList());
    }

    /// <summary>
    /// A plugin's own section sorts after every one the engine names rather than
    /// wherever the load order put it — and is not refused, because a plugin may
    /// well add a kind of module the engine has no word for.
    /// </summary>
    [Fact]
    public void A_section_the_engine_does_not_name_sorts_last()
    {
        var provider = new ModuleProvider("test.strange", "Strange");

        var module = new NodeDef(
            "test.strange.thing", "Thing", "Granular Resynthesis",
            [new PortSpec("in")],
            [new PortSpec("out")],
            (em, i) => [em.Mul(i[0], 2f)]);

        var added = NodeCatalog.BuiltIn.With(provider, [module]);

        added.Rejected.ShouldBeEmpty();
        added.Catalog.Categories.Last().ShouldBe("Granular Resynthesis");
    }

    /// <summary>
    /// No two sections are the same word, which is what "Space" meaning two
    /// things had made possible.
    /// </summary>
    [Fact]
    public void No_two_sections_share_a_name() =>
        ModuleCategories.All.Distinct().Count().ShouldBe(ModuleCategories.All.Count);
}
