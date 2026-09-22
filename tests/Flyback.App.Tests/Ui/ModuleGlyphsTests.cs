using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The marks set across a module's body. Artwork mostly, with three things in it
/// that can go wrong without anybody looking at a canvas: a category with no
/// mark, a mark keyed to a module that is not there, and two modules drawn the
/// same.
/// </summary>
public class ModuleGlyphsTests : UiTest
{
    /// <summary>
    /// The same promise <see cref="ColorsTests"/> makes about the accents: a
    /// category the engine ships draws as itself rather than as nothing.
    /// </summary>
    [AvaloniaFact]
    public void Every_category_the_engine_ships_has_a_mark()
    {
        foreach (var category in NodeCatalog.BuiltIn.Categories)
            ModuleGlyphs.OfCategory(category).ShouldNotBeNull($"'{category}' has no mark of its own");
    }

    /// <summary>
    /// A mark keyed to a type id nothing answers to is a drawing nobody will ever
    /// see, and renaming a module is exactly how one gets there.
    /// </summary>
    [AvaloniaFact]
    public void Every_mark_of_its_own_names_a_module_that_exists()
    {
        foreach (var typeId in ModuleGlyphs.Named)
            NodeCatalog.BuiltIn.Get(typeId).ShouldNotBeNull($"nothing in the catalog is called '{typeId}'");
    }

    /// <summary>
    /// The point of a mark of its own is that it is its own. Two modules sharing
    /// one would be a copied line rather than a decision.
    /// </summary>
    [AvaloniaFact]
    public void No_module_is_drawn_as_another()
    {
        var drawn = ModuleGlyphs.Named
            .Select(typeId => (TypeId: typeId, Mark: ModuleGlyphs.For(NodeCatalog.BuiltIn.Require(typeId))))
            .ToArray();

        foreach (var shared in drawn.GroupBy(d => d.Mark).Where(g => g.Count() > 1))
            throw new ShouldAssertException(
                $"{string.Join(" and ", shared.Select(s => s.TypeId))} are drawn with the same mark");
    }

    /// <summary>
    /// Avalonia draws nothing for a path it could not read and says nothing about
    /// it, so a typo in the artwork is invisible until somebody looks. Every mark
    /// has to be a shape with some size to it.
    /// </summary>
    [AvaloniaFact]
    public void Every_mark_is_a_shape_that_fits_the_box_it_is_drawn_on()
    {
        var marks = NodeCatalog.BuiltIn.Categories
            .Select(c => (Name: c, Mark: ModuleGlyphs.OfCategory(c)))
            .Concat(ModuleGlyphs.Named.Select(t =>
                (Name: t, Mark: ModuleGlyphs.For(NodeCatalog.BuiltIn.Require(t)))))
            .Append((Name: "a group", Mark: (Geometry?)ModuleGlyphs.Group));

        foreach (var (name, mark) in marks)
        {
            var bounds = mark.ShouldNotBeNull().GetRenderBounds(
                new Pen(Brushes.White, ModuleGlyphs.Thickness));

            bounds.Width.ShouldBeGreaterThan(ModuleGlyphs.Box / 3, $"{name} is barely a shape at all");
            bounds.Height.ShouldBeGreaterThan(ModuleGlyphs.Box / 3, $"{name} is barely a shape at all");

            // Set in the body by one scale and nothing else, so a path that
            // wanders outside its box is drawn outside the module.
            bounds.X.ShouldBeGreaterThanOrEqualTo(-1, $"{name} is drawn off the left of its box");
            bounds.Y.ShouldBeGreaterThanOrEqualTo(-1, $"{name} is drawn off the top of its box");
            bounds.Right.ShouldBeLessThanOrEqualTo(ModuleGlyphs.Box + 1, $"{name} is drawn off the right");
            bounds.Bottom.ShouldBeLessThanOrEqualTo(ModuleGlyphs.Box + 1, $"{name} is drawn off the bottom");
        }
    }
}
