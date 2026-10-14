using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Tutorials;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>Lists the tutorials, says one's steps, or checks a patch against them as the editor's column does (ADR-0196).</summary>
internal static class TutorialCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var name = new Argument<string?>("tutorial")
        {
            Description = "A tutorial, by id or title. Left out, list them.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var patch = new Argument<FileInfo?>("patch")
        {
            Description = $"A patch to check against each step: .{PatchIO.FileExtension}, {PatchBundle.Extension} or .{PatchLanguage.FileExtension}. "
                + "Left out, say the steps.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var step = new Option<int?>("--step")
        {
            Description = "Check this step alone, counting from 1.",
        };

        var command = new Command("tutorial", "List the tutorials, say one's steps, or check a patch against them.")
        {
            name, patch, step, json,
        };

        command.SetAction(result =>
        {
            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;
            var asJson = result.GetValue(json);

            if (result.GetValue(name) is not { } wanted) return List(asJson, output);

            if (ShippedTutorials.Find(wanted) is not { } tutorial)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: there is no tutorial '{wanted}'. "
                    + $"There are: {string.Join(", ", ShippedTutorials.Order)}.");
                return Exit.Failed;
            }

            if (result.GetValue(patch) is not { } file) return Describe(tutorial, asJson, output);

            plugins.Ready();

            return Patches.Open(file, error) is { } opened
                ? Check(tutorial, opened.Patch, NodeCatalog.Current, result.GetValue(step), asJson, output, error)
                : Exit.Failed;
        });

        return command;
    }

    public static int List(bool json, TextWriter output)
    {
        var all = ShippedTutorials.All;

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                all.Select(t => new { t.Id, t.Title, t.Summary, t.From, steps = t.Steps.Count }),
                Writing.Json));
            return Exit.Ok;
        }

        var wide = all.Max(t => t.Id.Length);
        foreach (var tutorial in all)
            output.WriteLine($"{tutorial.Id.PadRight(wide)}  {tutorial.Title}  ({Writing.Count(tutorial.Steps.Count, "step")}, from {tutorial.From})");

        return Exit.Ok;
    }

    public static int Describe(Tutorial tutorial, bool json, TextWriter output)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(tutorial, Writing.Json));
            return Exit.Ok;
        }

        output.WriteLine(tutorial.Title);
        output.WriteLine($"  {tutorial.Summary} Opens on {tutorial.From}.");

        for (var i = 0; i < tutorial.Steps.Count; i++)
        {
            var step = tutorial.Steps[i];
            output.WriteLine();
            output.WriteLine($"{i + 1}. {step.Title}");
            output.WriteLine($"   {step.Text}");
            if (step.Goal is not null)
                foreach (var line in step.Goal.Split('\n'))
                    output.WriteLine($"     {line}");
        }

        return Exit.Ok;
    }

    /// <summary>Each step with a goal, met or not; <see cref="Exit.Problems"/> when any checked step is not.</summary>
    public static int Check(Tutorial tutorial, Patch patch, ModuleCatalog catalog, int? only, bool json, TextWriter output, TextWriter error)
    {
        if (only is { } n && (n < 1 || n > tutorial.Steps.Count))
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: '{tutorial.Title}' has steps 1 to {tutorial.Steps.Count}.");
            return Exit.Failed;
        }

        var checks = tutorial.Steps
            .Select((step, i) => (Number: i + 1, Step: step))
            .Where(s => only is null ? s.Step.Goal is not null : s.Number == only)
            .Select(s => (s.Number, s.Step, Verdict: s.Step.Goal is { } goal ? StepCheck.Against(patch, goal, catalog) : null))
            .ToList();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                checks.Select(c => new { step = c.Number, c.Step.Title, checkable = c.Verdict is not null, done = c.Verdict?.Done, hint = c.Verdict?.Hint }),
                Writing.Json));
        }
        else
        {
            foreach (var (number, step, verdict) in checks)
            {
                var mark = verdict is null ? "-" : verdict.Done ? "✓" : "✗";
                output.WriteLine($"{mark} {number}. {step.Title}{(verdict?.Hint is { } hint ? $"  {hint}" : verdict is null ? "  (read, nothing to check)" : "")}");
            }
        }

        return checks.All(c => c.Verdict?.Done != false) ? Exit.Ok : Exit.Problems;
    }
}
