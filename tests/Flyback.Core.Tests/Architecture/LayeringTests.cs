using System.Xml.Linq;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// The projects depend downward only: the model, then the engine, then what hosts
/// need, then the shells (the layers in <c>docs/diagrams/editor.md</c>).
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
public class LayeringTests
{
    private static readonly string[] Model = ["Flyback.Core"];
    private static readonly string[] Engine = ["Flyback.Engine"];
    private static readonly string[] Hosting = ["Flyback.Plugins", "Flyback.Gpu", "Flyback.Host", "Flyback.Assist"];

    [Fact]
    public void The_model_references_no_project()
    {
        foreach (var project in Model)
            References(project).ShouldBeEmpty($"{project} is the bottom layer");
    }

    [Fact]
    public void The_engine_references_only_the_model()
    {
        foreach (var project in Engine)
            References(project).Except(Model).ShouldBeEmpty($"{project} sits on the model alone");
    }

    [Fact]
    public void Hosting_projects_reference_nothing_above_them()
    {
        foreach (var project in Hosting)
            References(project)
                .Except(Model).Except(Engine).Except(Hosting)
                .ShouldBeEmpty($"{project} must not reach a shell or the UI");
    }

    [Fact]
    public void The_model_and_the_engine_take_no_package_but_an_analyzer()
    {
        foreach (var project in Model.Concat(Engine))
            Packages(project).ShouldBeEmpty($"{project} takes no dependencies (ADR-0019)");
    }

    [Fact]
    public void The_command_line_reaches_no_window()
    {
        var closure = Closure("Flyback.Cli");

        closure.Except(Model).Except(Engine).Except(Hosting).Except(["Flyback.Cli", "Flyback.Site.Client"])
            .ShouldBeEmpty("flyback-cli runs where there is no display, so nothing it loads may be a shell or the UI");
        closure.SelectMany(Packages)
            .Where(p => p.StartsWith("Avalonia", StringComparison.Ordinal) || p.StartsWith("SkiaSharp", StringComparison.Ordinal) || p.StartsWith("HarfBuzzSharp", StringComparison.Ordinal))
            .ShouldBeEmpty("flyback-cli runs where there is no display, so it takes no windowing or font package");
    }

    private static string[] Closure(string project)
    {
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        var waiting = new Stack<string>([project]);
        while (waiting.TryPop(out var next))
            if (seen.Add(next))
                foreach (var reference in References(next))
                    waiting.Push(reference);
        return seen.ToArray();
    }

    private static string[] References(string project) =>
        Load(project).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(((string)r.Attribute("Include")!).Replace('\\', '/')))
            .Order()
            .ToArray();

    private static string[] Packages(string project) =>
        Load(project).Descendants("PackageReference")
            .Where(p => !string.Equals((string?)p.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase))
            .Select(p => (string)p.Attribute("Include")!)
            .Order()
            .ToArray();

    private static XDocument Load(string project) =>
        XDocument.Load(Repository.Path("src", project, project + ".csproj"));
}
