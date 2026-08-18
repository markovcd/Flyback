using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The palette. Mostly a list of values with nothing to test, except for the one
/// lookup in it that can be wrong without anybody noticing: a module category
/// nothing has an accent for still draws, in grey.
/// </summary>
public class ColoursTests
{
    /// <summary>
    /// Every category the engine ships. A new one added to the catalogue without
    /// a colour beside it would look like a mistake rather than a category, and
    /// nothing else in the program would say so.
    /// </summary>
    [Fact]
    public void Every_category_the_engine_ships_has_its_own_accent()
    {
        foreach (var category in NodeCatalog.BuiltIn.Categories)
            Colours.Accent(category).ShouldNotBe(
                Colours.Unknown, $"'{category}' has no colour of its own");
    }

    /// <summary>
    /// And a category from somewhere else falls back rather than throwing — a
    /// plugin may introduce one, and ADR-0025 promises that nothing a plugin
    /// does takes the shell down.
    /// </summary>
    [Fact]
    public void A_category_from_a_plugin_falls_back_to_grey() =>
        Colours.Accent("Granular Resynthesis").ShouldBe(Colours.Unknown);

    [Theory]
    [InlineData(PortKind.Colour)]
    [InlineData(PortKind.Any)]
    [InlineData(PortKind.Scalar)]
    public void Every_kind_of_socket_is_told_apart_by_colour(PortKind kind)
    {
        var others = Enum.GetValues<PortKind>()
            .Where(k => k != kind)
            .Select(Colours.PortColour);

        Colours.PortColour(kind).ShouldNotBeOneOf([.. others]);
    }

    /// <summary>
    /// The palette is what stops two greys drifting a shade apart, which is
    /// exactly what had happened to the two it replaced. Every entry earns its
    /// place by being distinct from every other.
    /// </summary>
    [Fact]
    public void No_two_entries_are_the_same_colour()
    {
        var named = typeof(Colours)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(Color))
            .Select(p => (p.Name, Colour: (Color)p.GetValue(null)!))
            .ToArray();

        named.Length.ShouldBeGreaterThan(20, "the palette should be the whole palette");

        foreach (var duplicated in named.GroupBy(n => n.Colour).Where(g => g.Count() > 1))
            throw new ShouldAssertException(
                $"{string.Join(" and ", duplicated.Select(d => d.Name))} are the same colour — "
                + "one of them should be defined in terms of the other");
    }
}
