using Avalonia.Media;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Ui.Tests.Controls;

/// <summary>
/// The palette. Mostly a list of values with nothing to test, except for the one
/// lookup in it that can be wrong without anybody noticing: a module category
/// nothing has an accent for still draws, in gray.
/// </summary>
public class ColorsTests
{
    /// <summary>
    /// Every category the engine ships. A new one added to the catalog without
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
    public void A_category_from_a_plugin_falls_back_to_gray() =>
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
    /// The palette exists to stop two grays drifting a shade apart. Every entry
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

    /// <summary>
    /// A socket's name is held to WCAG's 4.5:1 on a module, its number and a muted line
    /// to the 3:1 for secondary text, and the accent to the 3:1 a control needs on the window.
    /// </summary>
    [Theory]
    [InlineData(ColorScheme.Phosphor)]
    [InlineData(ColorScheme.Amber)]
    [InlineData(ColorScheme.Graphite)]
    [InlineData(ColorScheme.Midnight)]
    public void Every_scheme_can_be_read(ColorScheme name)
    {
        var scheme = Scheme.Of(name);

        Contrast(scheme.Label, scheme.Node).ShouldBeGreaterThanOrEqualTo(4.5);
        Contrast(scheme.Value, scheme.Node).ShouldBeGreaterThanOrEqualTo(3);
        Contrast(scheme.Muted, scheme.Panel).ShouldBeGreaterThanOrEqualTo(3);
        Contrast(scheme.Attention, scheme.Window).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Each_scheme_has_an_accent_of_its_own() =>
        Enum.GetValues<ColorScheme>().Select(name => Scheme.Of(name).Attention).Distinct()
            .Count().ShouldBe(Enum.GetValues<ColorScheme>().Length);

    [Fact]
    public void A_scheme_this_build_does_not_know_is_phosphor() =>
        Scheme.Of((ColorScheme)99).ShouldBe(Scheme.Phosphor);

    /// <summary>WCAG's contrast ratio, from one to twenty-one.</summary>
    private static double Contrast(Color one, Color other)
    {
        var (a, b) = (Luminance(one), Luminance(other));

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        var c = channel / 255d;

        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
