using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// The editor's interface is drawn in its color scheme: no color written out in the code,
/// and no module family's color standing for something other than a module.
/// </summary>
/// <remarks>
/// A shadow, a casing or a glint (pure black or white with some alpha) is no color of
/// its own, so it may be written out. The logo and its studio mark keep their colors in
/// every scheme.
/// </remarks>
[TestCategory(TestCategory.Unmeasured)]
public partial class SchemeColorTests
{
    /// <summary>Where colors are defined rather than used.</summary>
    private static readonly string[] Palette = ["Colors.cs", "SchemeFile.cs"];

    /// <summary>Drawings that are the same in every scheme.</summary>
    private static readonly string[] Marks = ["LogoBeam.cs", "LogoMark.cs", "StudioMark.cs", "QrCode.cs"];

    [Fact]
    public void No_color_is_written_out_in_the_interface() =>
        Offenders(Written(), file => Palette.Contains(file) || Marks.Contains(file), Glint)
            .ShouldBeEmpty("the interface takes its colors from the scheme (Colors.cs); one written out stays the same in every scheme");

    [Fact]
    public void A_module_family_color_stands_only_for_a_module() =>
        Offenders(Family(), file => Palette.Contains(file) || Marks.Contains(file), _ => false)
            .ShouldBeEmpty("a family color says what a module is; anywhere else the scheme's accent, text or danger says it");

    private static List<string> Offenders(Regex pattern, Func<string, bool> allowed, Func<string, bool> excused) =>
        [.. Sources()
            .Where(path => !allowed(Path.GetFileName(path)))
            .SelectMany(path => File.ReadLines(path).Select((line, i) => (path, line, number: i + 1)))
            .Where(at => pattern.IsMatch(at.line) && !excused(at.line))
            .Select(at => $"{Path.GetRelativePath(Repository.Root, at.path)}:{at.number}: {at.line.Trim()}")];

    private static bool Glint(string line) => Gray().Count(line) == Written().Count(line);

    private static IEnumerable<string> Sources() =>
        new[] { "Flyback.Editor", "Flyback.Ui" }
            .SelectMany(project => Directory.EnumerateFiles(Repository.Path("src", project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"));

    [GeneratedRegex(@"Color\.From(?:Rgb|Argb)\(\s*(0x|\d)|Color\.Parse\(\s*""#")]
    private static partial Regex Written();

    [GeneratedRegex(@"Color\.FromArgb\([^,()]+,\s*(?:(?:0|0x00),\s*(?:0|0x00),\s*(?:0|0x00)|255,\s*255,\s*255)\s*\)")]
    private static partial Regex Gray();

    [GeneratedRegex(@"\bColors\.(Sink|Source|Oscillator|Sequencer|Maths|Space|Pattern|Tint|Feedback|Form|Note|Shaping|Echo|Reading|Routing|Unknown|BeamCore|ColorPort|AnyPort|ScalarPort)\b")]
    private static partial Regex Family();
}
