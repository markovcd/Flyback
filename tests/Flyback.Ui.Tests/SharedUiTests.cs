using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests;

/// <summary>
/// <c>Flyback.Ui</c> holds what both windows draw and sound with (ADR-0124), so every
/// type in it is named by a shell other than the editor, or by a Ui type that is. A
/// type only the editor names belongs in the editor.
/// </summary>
/// <remarks>
/// Read off the source, as <c>ComponentDiagramTests</c> reads the project files: a type
/// is its file's name, and naming is a whole-word match. The shells are the viewer,
/// the page, the Android program and the desktop program; the editor is left out,
/// since it is the one that would hoard.
/// </remarks>
public sealed class SharedUiTests
{
    /// <summary>Extension classes the shells reach by method name, never by type.</summary>
    private static readonly Dictionary<string, string> Reached = new()
    {
        ["GraphicsDrivers"] = "the shells pick a driver through its extension methods",
        ["TransportServices"] = "the editor and the viewer register the transport through AddTransport",
    };

    [Fact]
    public void Every_type_in_Ui_is_named_by_a_shell_other_than_the_editor_or_by_a_Ui_type_that_is()
    {
        var ui = Sources(Repository.Path("src", "Flyback.Ui"))
            .Where(file => !file.EndsWith("AssemblyInfo.cs", StringComparison.Ordinal))
            .ToDictionary(file => Path.GetFileName(file).Split('.')[0], File.ReadAllText);
        var shells = string.Join('\n',
            new[] { "Flyback.Viewer.Desktop", "Flyback.Editor.Web", "Flyback.Editor.Android", "Flyback.Editor.Desktop" }
                .SelectMany(project => Sources(Repository.Path("src", project)))
                .Select(File.ReadAllText));

        var shared = ui.Keys.Where(type => Names(shells, type)).ToHashSet();
        for (var grew = true; grew;)
            grew = ui.Keys.Where(type => !shared.Contains(type) && shared.Any(by => Names(ui[by], type))).Aggregate(false, (_, type) => shared.Add(type));

        ui.Keys.Except(shared).Except(Reached.Keys).Order()
            .ShouldBeEmpty("nothing but the editor names these; each belongs in the editor's folder for its feature (ADR-0124)");
        Reached.Keys.Intersect(shared).Order()
            .ShouldBeEmpty("a shell names these by type now, and the Reached list no longer needs them");
    }

    private static IEnumerable<string> Sources(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static bool Names(string text, string type) => Regex.IsMatch(text, $@"\b{Regex.Escape(type)}\b");
}
