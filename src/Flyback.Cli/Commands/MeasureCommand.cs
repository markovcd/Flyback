using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Measure;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Runs a patch offline for a few seconds and says what every output carried, to the
/// speakers and to the screen.
/// </summary>
internal static class MeasureCommand
{
    /// <summary>Runs a patch offline and says what every output carried.</summary>
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<string>("patch")
        {
            Description = "The patch to measure: a document, a bundle, or one written as text. "
                + "Left out with --preset, whose outputs then follow at once.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var outputs = new Argument<string[]>("outputs")
        {
            Description = "Which outputs: a module for all of its outputs, or module.output for one. "
                + "Two modules with one title are numbered in patch order: 'Oscillator 2'. Left out, every output.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "A shipped preset, by name, in place of a file.",
        };

        var seconds = new Option<double>("--seconds")
        {
            Description = "How long to run the patch.",
            DefaultValueFactory = _ => MeasureOptions.DefaultSeconds,
        };

        var from = new Option<double>("--from")
        {
            Description = "Where on the patch's clock to start, in seconds. Memory starts empty there.",
        };

        var command = new Command(
            "measure",
            "Run a patch offline and say what every output carries, to the speakers and to the screen: "
            + "its value, or its range and how fast it changes.")
        {
            patch, outputs, preset, seconds, from, json,
        };

        command.SetAction((result, cancellation) =>
        {
            plugins.Ready();

            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;
            var first = result.GetValue(patch);
            var named = result.GetValue(outputs) ?? [];
            var shipped = result.GetValue(preset);

            Opened? opened;

            if (shipped is not null)
            {
                // The first word was an output, there being no file to name.
                if (first is not null) named = [first, .. named];

                opened = ShippedPresets.Open(plugins.Catalog, shipped, error)?.Opened;
            }
            else if (first is not null)
            {
                opened = Patches.Open(new FileInfo(first), error);
            }
            else
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to measure: a patch, or --preset and its name.");

                return Task.FromResult(Exit.Failed);
            }

            if (opened is not { } found) return Task.FromResult(Exit.Failed);

            return Task.FromResult(MeasureCommand.Run(
                found.Patch,
                named,
                new MeasureOptions(result.GetValue(seconds), result.GetValue(from)),
                result.GetValue(json),
                NodeCatalog.Current,
                output,
                error,
                found.Samples,
                found.Pictures,
                result.GetValue(json) ? null : ConsoleProgress.For("measuring"),
                cancellation));
        });

        return command;
    }

    /// <param name="patch"></param>
    /// <param name="named">
    /// Which outputs: a module's handle for all of its outputs, or a handle, a dot and an
    /// output's name. Empty for every output of every module.
    /// </param>
    /// <param name="options">The window. Its modules are filled in from <paramref name="named"/>.</param>
    /// <param name="json"></param>
    /// <param name="catalog"></param>
    /// <param name="output"></param>
    /// <param name="error"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    /// <param name="progress"></param>
    /// <param name="cancel"></param>
    public static int Run(
        Patch patch,
        IReadOnlyList<string> named,
        MeasureOptions options,
        bool json,
        ModuleCatalog catalog,
        TextWriter output,
        TextWriter error,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        if (!(options.Seconds > 0d && options.Seconds <= MeasureOptions.MaxSeconds))
        {
            error.WriteLine($"--seconds runs above 0 and up to {MeasureOptions.MaxSeconds}.");

            return Exit.Failed;
        }

        if (!double.IsFinite(options.From) || options.From < 0d)
        {
            error.WriteLine("--from is 0 or later.");

            return Exit.Failed;
        }

        var handles = Handles(patch, catalog);
        var wanted = new List<(Guid Node, int? Port)>();

        foreach (var name in named)
        {
            if (Find(name, handles, catalog, patch, out var refusal) is not { } found)
            {
                error.WriteLine(refusal);

                return Exit.Failed;
            }

            wanted.Add(found);
        }

        MeasureReport report;

        try
        {
            report = Measurements.Take(
                patch,
                options with { Modules = wanted.Count == 0 ? null : [.. wanted.Select(w => w.Node).Distinct()] },
                catalog,
                samples,
                pictures,
                progress,
                cancel);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Stopped. Nothing was measured.");

            return Exit.Failed;
        }

        var kept = report.Measurements
            .Where(m => wanted.Count == 0 || wanted.Any(w => w.Node == m.Node && (w.Port is null || w.Port == m.Port)))
            .ToArray();

        var nameOf = handles.ToDictionary(pair => pair.Value, pair => pair.Key);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    from = report.From,
                    seconds = report.Seconds,
                    measurements = kept.Select(m => new
                    {
                        module = nameOf.GetValueOrDefault(m.Node, m.Module),
                        socket = m.Socket,
                        node = m.Node,
                        port = m.Port,
                        differs = m.Differs,
                        sound = m.Sound,
                        picture = m.Picture,
                    }),
                    issues = report.Issues,
                },
                Writing.Json));

            return Exit.Ok;
        }

        output.WriteLine(
            $"Measured {MeasurementWords.Number(report.Seconds)} s from {MeasurementWords.Number(report.From)} s, with nothing played in.");

        if (kept.Length == 0) output.WriteLine("Nothing here has an output to measure.");

        foreach (var m in kept)
        {
            output.WriteLine();
            output.WriteLine($"{nameOf.GetValueOrDefault(m.Node, m.Module)}.{m.Socket}{(m.Differs ? "  (sound and picture differ)" : string.Empty)}");

            foreach (var line in MeasurementWords.Half("sound", m.Sound, report.Seconds)) output.WriteLine("  " + line);
            foreach (var line in MeasurementWords.Half("picture", m.Picture, report.Seconds)) output.WriteLine("  " + line);
        }

        return Exit.Ok;
    }

    /// <summary>
    /// What each module is called here: its title, numbered in patch order where two
    /// share one.
    /// </summary>
    internal static Dictionary<string, Guid> Handles(Patch patch, ModuleCatalog catalog)
    {
        var titled = patch.Nodes
            .Select(node => (node, def: catalog.Get(node.TypeId)))
            .Where(pair => pair.def is not null)
            .Select(pair => (pair.node.Id, Title: pair.node.Title(pair.def!)))
            .ToArray();

        var shared = titled
            .GroupBy(pair => pair.Title, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var handles = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, title) in titled)
        {
            var handle = title;

            if (shared.Contains(title))
            {
                seen[title] = seen.GetValueOrDefault(title) + 1;
                handle = $"{title} {seen[title]}";
            }

            handles.TryAdd(handle, id);
        }

        return handles;
    }

    private static (Guid Node, int? Port)? Find(
        string name,
        Dictionary<string, Guid> handles,
        ModuleCatalog catalog,
        Patch patch,
        out string refusal)
    {
        refusal = string.Empty;

        if (handles.TryGetValue(name.Trim(), out var whole)) return (whole, null);

        var dot = name.LastIndexOf('.');

        if (dot > 0 && handles.TryGetValue(name[..dot].Trim(), out var node))
        {
            var socket = name[(dot + 1)..].Trim();
            var def = catalog.Require(patch.Find(node)!.TypeId);

            for (var port = 0; port < def.Outputs.Count; port++)
                if (string.Equals(def.Outputs[port].Name, socket, StringComparison.OrdinalIgnoreCase))
                    return (node, port);

            refusal = def.Outputs.Count == 0
                ? $"'{name[..dot]}' has no outputs."
                : $"'{name[..dot]}' has no output called '{socket}'. It has {string.Join(", ", def.Outputs.Select(o => o.Name))}.";

            return null;
        }

        refusal = $"No module here is called '{name}'. The modules are {string.Join(", ", handles.Keys.Select(k => $"'{k}'"))}.";

        return null;
    }
}
