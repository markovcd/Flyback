using Avalonia.Media;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The palette. Mostly a list of values with nothing to test, except for the one
/// lookup in it that can be wrong without anybody noticing: a module category
/// nothing has an accent for still draws, in grey.
/// </summary>
public class ColorsTests
{
    /// <summary>
    /// Every category the engine ships. A new one added to the catalogue without
    /// a color beside it would look like a mistake rather than a category, and
    /// nothing else in the program would say so.
    /// </summary>
    [Fact]
    public void Every_category_the_engine_ships_has_its_own_accent()
    {
        foreach (var category in NodeCatalog.BuiltIn.Categories)
            Colors.Accent(category).ShouldNotBe(
                Colors.Unknown, $"'{category}' has no color of its own");
    }

    /// <summary>
    /// And a category from somewhere else falls back rather than throwing — a
    /// plugin may introduce one, and ADR-0025 promises that nothing a plugin
    /// does takes the shell down.
    /// </summary>
    [Fact]
    public void A_category_from_a_plugin_falls_back_to_grey() =>
        Colors.Accent("Granular Resynthesis").ShouldBe(Colors.Unknown);

    [Theory]
    [InlineData(PortKind.Color)]
    [InlineData(PortKind.Any)]
    [InlineData(PortKind.Scalar)]
    public void Every_kind_of_socket_is_told_apart_by_color(PortKind kind)
    {
        var others = Enum.GetValues<PortKind>()
            .Where(k => k != kind)
            .Select(Colors.PortColor);

        Colors.PortColor(kind).ShouldNotBeOneOf([.. others]);
    }

    /// <summary>
    /// The palette exists to stop two greys drifting a shade apart. Every entry
    /// earns its place by being distinct from every other.
    /// </summary>
    [Fact]
    public void No_two_entries_are_the_same_color()
    {
        var named = typeof(Colors)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(Color))
            .Select(p => (p.Name, Color: (Color)p.GetValue(null)!))
            .ToArray();

        named.Length.ShouldBeGreaterThan(20, "the palette should be the whole palette");

        foreach (var duplicated in named.GroupBy(n => n.Color).Where(g => g.Count() > 1))
            throw new ShouldAssertException(
                $"{string.Join(" and ", duplicated.Select(d => d.Name))} are the same color — "
                + "one of them should be defined in terms of the other");
    }
}
