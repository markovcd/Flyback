using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Lists the groups, def calls, loops and loop passes a text patch opened, inside one
/// another, with the modules each placed.
/// </summary>
internal static class LevelsCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<FileInfo>("patch") { Description = "The patch, written as text (." + PatchLanguage.FileExtension + ")." };

        var of = new Option<string>("--of")
        {
            Description = "A module, by name: print only the levels it sits in, from the outermost down.",
        };

        var command = new Command("levels", "List the groups, def calls and loop passes a text patch opens, and the modules in each.")
        {
            patch, of, json,
        };

        command.SetAction(result =>
        {
            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;
            var file = result.GetValue(patch)!;

            plugins.Ready();

            if (!Patches.Sourced(file))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName} levels: {file.Name} is not written as text; levels come from the text.");
                return Exit.Failed;
            }

            if (!file.Exists)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName} levels: {file.Name} does not exist.");
                return Exit.Failed;
            }

            var read = PatchLanguage.Build(File.ReadAllText(file.FullName));

            if (!read.Ok) return CheckCommand.Unread(read.Issues, file.Name, result.GetValue(json), output, error);

            if (result.GetValue(of) is not { } name) return Listed(read, result.GetValue(json), output);

            var module = read.Patch.Nodes.FirstOrDefault(n => Named(read.Patch, n.Id) == name)
                ?? read.Patch.Nodes.FirstOrDefault(n => string.Equals(Named(read.Patch, n.Id), name, StringComparison.OrdinalIgnoreCase));

            if (module is null)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName} levels: no module is called '{name}'.");
                return Exit.Failed;
            }

            var path = Level.PathTo(read.Levels, module.Id) ?? [];

            if (result.GetValue(json))
            {
                output.WriteLine(JsonSerializer.Serialize(path.Select(level => Shown(read.Patch, level, inside: false)), Writing.Json));
            }
            else
            {
                output.WriteLine(path.Count == 0 ? "(top)" : string.Join(" › ", path.Select(level => level.Label)));
            }

            return Exit.Ok;
        });

        return command;
    }

    private static int Listed(LanguageLoad read, bool json, TextWriter output)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(read.Levels.Select(level => Shown(read.Patch, level, inside: true)), Writing.Json));
            return Exit.Ok;
        }

        foreach (var level in read.Levels) Write(read.Patch, level, 0, output);

        return Exit.Ok;
    }

    private static void Write(Patch patch, Level level, int depth, TextWriter output)
    {
        var indent = new string(' ', depth * 2);
        var item = level.Item is null ? "" : $"  {level.Item}";
        var members = level.Members.Count == 0 ? "" : $"  [{string.Join(", ", level.Members.Select(id => Named(patch, id)))}]";

        output.WriteLine($"{indent}{Kind(level.Kind),-5} {level.Label}{item}  line {level.Line}{members}");

        foreach (var inner in level.Inside) Write(patch, inner, depth + 1, output);
    }

    private static object Shown(Patch patch, Level level, bool inside) => new
    {
        kind = Kind(level.Kind),
        name = level.Name,
        label = level.Label,
        binding = level.Binding,
        pass = level.Pass,
        passes = level.Passes,
        item = level.Item,
        line = level.Line,
        column = level.Column,
        modules = level.Members.Select(id => Named(patch, id)).ToArray(),
        inside = inside ? level.Inside.Select(inner => Shown(patch, inner, inside: true)).ToArray() : null,
    };

    private static string Kind(LevelKind kind) => kind switch
    {
        LevelKind.Group => "group",
        LevelKind.Call => "call",
        LevelKind.Loop => "loop",
        _ => "pass",
    };

    private static string Named(Patch patch, Guid id) =>
        patch.Find(id) is { } node ? NodeCatalog.Get(node.TypeId) is { } def ? node.Title(def) : node.TypeId : id.ToString();
}
