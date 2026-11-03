using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Controls;

/// <summary>The schemes on offer: the shipped ones, and the user's folder beside them.</summary>
public sealed class SchemeLibraryTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-schemes");

    public void Dispose() => folder.Delete(recursive: true);

    private void Put(string file, string json) => File.WriteAllText(Path.Combine(folder.FullName, file), json);

    private static IEnumerable<string> Names(SchemeLibrary library) => library.Schemes.Select(scheme => scheme.Name);

    [Fact]
    public void Four_schemes_are_shipped_phosphor_first() =>
        SchemeLibrary.Shipped.Select(scheme => scheme.Name).ShouldBe(["Graphite", "Amber", "Midnight", "Paper", "Phosphor"]);

    /// <summary>Android's Mono sets a type up when it compiles a method building one, so a static on <see cref="Scheme"/> runs while the shipped schemes are read.</summary>
    [Fact]
    public void A_scheme_holds_no_static_state_for_reading_the_shipped_ones_to_run_into() =>
        typeof(Scheme).TypeInitializer.ShouldBeNull();

    [Fact]
    public void Without_a_folder_only_the_shipped_ones_are_offered() =>
        Names(SchemeLibrary.Load(null)).ShouldBe(["Graphite", "Amber", "Midnight", "Paper", "Phosphor"]);

    [Fact]
    public void A_scheme_in_the_folder_is_offered_after_the_shipped_ones()
    {
        Put("sodium.json", """{ "format": 1, "name": "Sodium", "accent": "#FF9A1F" }""");

        var library = SchemeLibrary.Load(folder.FullName);

        Names(library).ShouldBe(["Graphite", "Amber", "Midnight", "Paper", "Phosphor", "Sodium"]);
        library.Find("sodium").Attention.ShouldBe(Avalonia.Media.Color.FromRgb(0xFF, 0x9A, 0x1F));
        library.Problems.ShouldContain(line => line.StartsWith("sodium.json grounds.edge is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_scheme_in_the_folder_with_a_shipped_name_takes_its_place()
    {
        Put("amber.json", """{ "format": 1, "name": "amber", "accent": "#FFB040" }""");

        var library = SchemeLibrary.Load(folder.FullName);

        Names(library).ShouldBe(["Graphite", "amber", "Midnight", "Paper", "Phosphor"]);
        library.Find("Amber").Attention.ShouldBe(Avalonia.Media.Color.FromRgb(0xFF, 0xB0, 0x40));
    }

    [Fact]
    public void A_file_that_is_not_a_scheme_is_skipped_and_said()
    {
        Put("future.json", """{ "format": 2, "name": "Future" }""");
        Put("broken.json", "{");

        var library = SchemeLibrary.Load(folder.FullName);

        Names(library).ShouldBe(["Graphite", "Amber", "Midnight", "Paper", "Phosphor"]);
        library.Problems.ShouldContain("future.json is format 2, from a newer Flyback; this one reads format 1.");
        library.Problems.ShouldContain(line => line.StartsWith("broken.json is not JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void A_name_there_is_no_scheme_of_finds_the_first()
    {
        var library = SchemeLibrary.Load(null);

        library.Find("Teal").ShouldBe(SchemeLibrary.Default);
        library.Find(null).ShouldBe(SchemeLibrary.Default);
        library.Named("Teal").ShouldBeNull();
    }
}
