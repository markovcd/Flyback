using System.Text.RegularExpressions;
using System.Xml.Linq;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests;

/// <summary>
/// The editor's C3 view in <c>docs/diagrams/workspace.dsl</c> draws the projects the
/// editor is built from, so it is held to their project files: the same projects,
/// and an arrow for each reference not already reached through another.
/// </summary>
/// <remarks>
/// A component named for a project is that project. Arrows to anything else, and
/// the plugins the build copies beside the program, are drawn by hand.
/// </remarks>
public sealed partial class ComponentDiagramTests
{
    private const string Program = "Flyback.Editor.Desktop";

    [Fact]
    public void The_editors_components_are_the_projects_it_is_built_from()
    {
        var drawn = Drawn().Components.Values.ToHashSet();
        var built = References().Keys.ToHashSet();

        built.Except(drawn).Order().ShouldBeEmpty($"{Program} is built from these, and workspace.dsl has no component named for them");
        drawn.Except(built).Order().ShouldBeEmpty($"workspace.dsl has components named for these, and {Program} is not built from them");
    }

    [Fact]
    public void An_arrow_between_projects_is_a_reference_not_reached_another_way()
    {
        var (components, arrows) = Drawn();
        var drawn = arrows
            .Where(a => components.ContainsKey(a.From) && components.ContainsKey(a.To))
            .Select(a => $"{components[a.From]} -> {components[a.To]}")
            .ToHashSet();
        var needed = Needed(References()).ToHashSet();

        needed.Except(drawn).Order().ShouldBeEmpty("these references reach nothing another does, and workspace.dsl draws no arrow for them");
        drawn.Except(needed).Order().ShouldBeEmpty("workspace.dsl draws these arrows, and they are no reference, or one another reference already reaches");
    }

    /// <summary>Each project the program is built from, with the projects it references directly.</summary>
    private static Dictionary<string, string[]> References()
    {
        var src = Repository.Path("src");
        var references = new Dictionary<string, string[]>();
        var pending = new Stack<string>([Program]);

        while (pending.TryPop(out var name))
        {
            if (references.ContainsKey(name)) continue;

            var project = XDocument.Load(Path.Combine(src, name, name + ".csproj"));
            references[name] = project.Descendants("ProjectReference")
                .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))
                .ToArray();

            foreach (var referenced in references[name]) pending.Push(referenced);
        }

        return references;
    }

    /// <summary>The references no other reference already reaches.</summary>
    private static IEnumerable<string> Needed(Dictionary<string, string[]> references)
    {
        HashSet<string> Reached(string from)
        {
            var reached = new HashSet<string>();
            var pending = new Stack<string>(references[from]);
            while (pending.TryPop(out var name))
                if (reached.Add(name))
                    foreach (var next in references[name]) pending.Push(next);
            return reached;
        }

        return from project in references.Keys
               from to in references[project]
               where !references[project].Any(other => other != to && Reached(other).Contains(to))
               select $"{project} -> {to}";
    }

    /// <summary>The components named for projects, by identifier, and every arrow in the model.</summary>
    private static (Dictionary<string, string> Components, (string From, string To)[] Arrows) Drawn()
    {
        var model = File.ReadAllText(Repository.Path("docs", "diagrams", "workspace.dsl"));

        var components = ProjectComponent().Matches(model)
            .ToDictionary(m => m.Groups["id"].Value, m => m.Groups["name"].Value);
        var arrows = Arrow().Matches(model)
            .Select(m => (m.Groups["from"].Value, m.Groups["to"].Value))
            .ToArray();

        return (components, arrows);
    }

    [GeneratedRegex("""^\s*(?<id>\w+)\s*=\s*component\s+"(?<name>Flyback\.[\w.]+)"\s""", RegexOptions.Multiline)]
    private static partial Regex ProjectComponent();

    [GeneratedRegex("""^\s*(?<from>\w+)\s*->\s*(?<to>\w+)\s""", RegexOptions.Multiline)]
    private static partial Regex Arrow();
}
