using Avalonia.Media;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Controls;

/// <summary>A color scheme read from JSON: what is refused, and what stands in for a color a file lacks.</summary>
public sealed class SchemeFileTests
{
    private const string Sodium = """
        {
          // A comment and a trailing comma are fine.
          "format": 1,
          "name": " Sodium ",
          "grounds": { "window": "#120C06", "module": "#2A1C0E" },
          "text": { "label": "#F2D9B8" },
          "accent": "#ff9a1f",
        }
        """;

    private static (Scheme? Scheme, List<string> Problems) Read(string json, Scheme? fallback = null)
    {
        var problems = new List<string>();
        return (SchemeFile.Read(json, fallback ?? Scheme.Phosphor, problems), problems);
    }

    [Fact]
    public void Phosphor_is_shipped_as_the_green_of_the_beam_on_blue_gray()
    {
        Scheme.Phosphor.Name.ShouldBe("Phosphor");
        Scheme.Phosphor.Window.ShouldBe(Color.FromRgb(0x16, 0x18, 0x1B));
        Scheme.Phosphor.Attention.ShouldBe(Color.FromRgb(0x77, 0xEA, 0x9A));
    }

    [Fact]
    public void A_color_a_file_has_is_read_and_one_it_lacks_is_the_fallbacks_and_said()
    {
        var (scheme, problems) = Read(Sodium);

        scheme.ShouldNotBeNull();
        scheme.Name.ShouldBe("Sodium");
        scheme.Window.ShouldBe(Color.FromRgb(0x12, 0x0C, 0x06));
        scheme.Attention.ShouldBe(Color.FromRgb(0xFF, 0x9A, 0x1F));
        scheme.Canvas.ShouldBe(Scheme.Phosphor.Canvas);
        problems.ShouldContain("grounds.canvas is missing or not a #RRGGBB color, so Phosphor's is used.");
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#FF9A1F80")]
    [InlineData("FF9A1F")]
    [InlineData("#GG9A1F")]
    public void A_color_that_is_not_six_hex_digits_is_not_read(string accent)
    {
        var (scheme, problems) = Read($$"""{ "format": 1, "name": "X", "accent": "{{accent}}" }""");

        scheme.ShouldNotBeNull().Attention.ShouldBe(Scheme.Phosphor.Attention);
        problems.ShouldContain(line => line.StartsWith("accent is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_fallback_a_missing_color_refuses_the_file()
    {
        var problems = new List<string>();

        SchemeFile.Read(Sodium, fallback: null, problems).ShouldBeNull();
        problems.ShouldHaveSingleItem().ShouldStartWith("grounds.edge is missing");
    }

    [Fact]
    public void A_file_from_a_newer_flyback_is_refused_whole()
    {
        var (scheme, problems) = Read("""{ "format": 2, "name": "Future", "accent": "#FF9A1F" }""");

        scheme.ShouldBeNull();
        problems.ShouldHaveSingleItem().ShouldBe("is format 2, from a newer Flyback; this one reads format 1.");
    }

    [Theory]
    [InlineData("""{ "name": "No format" }""", "has no format number")]
    [InlineData("""{ "format": 0, "name": "Zero" }""", "has no format number")]
    [InlineData("""{ "format": 1 }""", "needs a name")]
    [InlineData("""{ "format": 1, "name": "   " }""", "needs a name")]
    [InlineData("""{ "format": 1, "name": "A name far longer than anybody would ever give one" }""", "needs a name")]
    [InlineData("""[1, 2]""", "holds no object")]
    [InlineData("""{ "format": 1, """, "is not JSON")]
    public void What_is_not_a_scheme_is_refused_with_why(string json, string why)
    {
        var (scheme, problems) = Read(json);

        scheme.ShouldBeNull();
        problems.ShouldHaveSingleItem().ShouldContain(why);
    }

    [Fact]
    public void A_file_too_large_to_be_a_scheme_is_refused_unread()
    {
        var (scheme, problems) = Read(new string(' ', SchemeFile.MostBytes + 1));

        scheme.ShouldBeNull();
        problems.ShouldHaveSingleItem().ShouldContain("larger than");
    }

    [Fact]
    public void Nesting_deeper_than_a_scheme_has_is_refused()
    {
        var (scheme, problems) = Read("""{ "format": 1, "name": "Deep", "grounds": { "window": { "x": { "y": { "z": 1 } } } } }""");

        scheme.ShouldBeNull();
        problems.ShouldHaveSingleItem().ShouldContain("is not JSON");
    }
}
