using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// The editor's interface is drawn in its color scheme: no color written out in the code,
/// and no module family's color standing for something other than a module.
/// </summary>
/// <remarks>
/// Only what is drawn over the picture is written in white and black, since the picture
/// is the same in every scheme; anywhere else a light scheme needs the opposite, which
/// <c>Colors.Lift</c>, <c>Colors.Press</c> and <c>Colors.InkOn</c> give. The logo and its
/// studio mark keep their colors in every scheme.
/// </remarks>
[TestCategory(TestCategory.Unmeasured)]
public partial class SchemeColorTests
{
    /// <summary>Where colors are defined rather than used.</summary>
    private static readonly string[] Palette = ["Colors.cs", "SchemeFile.cs"];

    /// <summary>Drawings that are the same in every scheme.</summary>
    private static readonly string[] Marks = ["LogoScene.cs", "LogoMark.cs", "StudioMark.cs", "QrCode.cs"];

    /// <summary>What is drawn over the picture, which no scheme colors.</summary>
    private static readonly string[] OverPicture =
        ["CanvasGlass.cs", "LivePatching.cs", "SeekTrack.cs", "StageKnob.cs", "StageSwitch.cs", "StatsOverlay.cs", "TuckedAway.cs"];

    private static bool Exempt(string file) => Palette.Contains(file) || Marks.Contains(file) || OverPicture.Contains(file);

    [Fact]
    public void No_color_is_written_out_in_the_interface() =>
        Offenders(Written(), Exempt)
            .ShouldBeEmpty("the interface takes its colors from the scheme (Colors.cs); one written out stays the same in every scheme");

    [Fact]
    public void White_and_black_are_written_only_over_the_picture() =>
        Offenders(Pole(), Exempt)
            .ShouldBeEmpty("a highlight, shadow or ink in white or black vanishes on one kind of scheme; Colors.Lift, Colors.Press and Colors.InkOn turn with the ground");

    [Fact]
    public void A_module_family_color_stands_only_for_a_module() =>
        Offenders(Family(), file => Palette.Contains(file) || Marks.Contains(file))
            .ShouldBeEmpty("a family color says what a module is; anywhere else the scheme's accent, text or danger says it");

    private static List<string> Offenders(Regex pattern, Func<string, bool> allowed) =>
        [.. Sources()
            .Where(path => !allowed(Path.GetFileName(path)))
            .SelectMany(path => File.ReadLines(path).Select((line, i) => (path, line, number: i + 1)))
            .Where(at => pattern.IsMatch(at.line))
            .Select(at => $"{Path.GetRelativePath(Repository.Root, at.path)}:{at.number}: {at.line.Trim()}")];

    private static IEnumerable<string> Sources() =>
        new[] { "Flyback.Editor", "Flyback.Ui" }
            .SelectMany(project => Directory.EnumerateFiles(Repository.Path("src", project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"));

    [GeneratedRegex(@"Color\.From(?:Rgb|Argb)\(\s*(0x|\d)|Color\.Parse\(\s*""#")]
    private static partial Regex Written();

    [GeneratedRegex(@"\b(?:Colors|Brushes)\.(?:White|Black)\b")]
    private static partial Regex Pole();

    [GeneratedRegex(@"\bColors\.(Sink|Source|Oscillator|Sequencer|Maths|Space|Pattern|Tint|Feedback|Form|Note|Shaping|Echo|Reading|Routing|Unknown|BeamCore|ColorPort|AnyPort|ScalarPort)\b")]
    private static partial Regex Family();
}
