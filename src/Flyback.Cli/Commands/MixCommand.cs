using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Mix;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>Listens to a patch for a few seconds and flags what usually goes wrong in a mix.</summary>
internal static class MixCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var given = new PatchInput("check the mix of");

        var seconds = new Option<double>("--seconds")
        {
            Description = $"How long to listen, up to {MixOptions.MaxSeconds}.",
            DefaultValueFactory = _ => MixOptions.DefaultSeconds,
        };

        var from = new Option<double>("--from")
        {
            Description = "Where on the patch's clock to start listening, in seconds. Put it past a quiet intro.",
        };

        var command = new Command(
            "mix",
            "Listen to a patch for a few seconds and flag the usual mix problems: no master chain, clipping, "
            + "DC offset, no sub, muddy low mids, a wide low end, a loudness far off, and sound sources that stay silent. "
            + "Exits 1 when anything is a warning.")
        {
            given.Patch, given.Preset, given.Presets, seconds, from, json,
        };

        command.SetAction((result, cancellation) =>
        {
            plugins.Ready();

            var asJson = result.GetValue(json);

            if (given.Listed(result, plugins, asJson)) return Task.FromResult(Exit.Ok);
            if (!given.Chosen(result, out var file, out var named)) return Task.FromResult(Exit.Failed);

            var error = result.InvocationConfiguration.Error;

            var opened = file is null
                ? ShippedPresets.Open(plugins.Catalog, named!, error)?.Opened
                : Patches.Open(file, error);

            if (opened is not { } found) return Task.FromResult(Exit.Failed);

            return Task.FromResult(Run(
                found.Patch,
                new MixOptions(result.GetValue(seconds), result.GetValue(from)),
                asJson,
                NodeCatalog.Current,
                result.InvocationConfiguration.Output,
                error,
                found.Samples,
                found.Pictures,
                asJson ? null : ConsoleProgress.For("listening"),
                cancellation));
        });

        return command;
    }

    public static int Run(
        Patch patch,
        MixOptions options,
        bool json,
        ModuleCatalog catalog,
        TextWriter output,
        TextWriter error,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        if (!(options.Seconds > 0d && options.Seconds <= MixOptions.MaxSeconds))
        {
            error.WriteLine($"--seconds runs above 0 and up to {MixOptions.MaxSeconds}.");
            return Exit.Failed;
        }

        if (!double.IsFinite(options.From) || options.From < 0d)
        {
            error.WriteLine("--from is 0 or later.");
            return Exit.Failed;
        }

        var handles = MeasureCommand.Handles(patch, catalog);
        var nameOf = handles.ToDictionary(pair => pair.Value, pair => pair.Key);

        MixReport report;

        try
        {
            report = MixCheck.Take(patch, options, catalog, samples, pictures, id => nameOf.GetValueOrDefault(id, id.ToString()), progress, cancel);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Stopped. Nothing was checked.");
            return Exit.Failed;
        }

        if (json)
        {
            var figures = report.Figures;

            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    from = report.From,
                    seconds = report.Seconds,
                    lufs = Finite(figures.Lufs),
                    truePeak = Finite(figures.TruePeak),
                    clips = figures.Clips,
                    sub = Finite(figures.Sub),
                    lowMids = Finite(figures.LowMids),
                    lowEndWidth = Finite(figures.LowEndWidth),
                    dcLeft = figures.DcLeft,
                    dcRight = figures.DcRight,
                    findings = report.Findings.Select(f => new
                    {
                        code = f.Code,
                        severity = f.Severity == MixSeverity.Warning ? "warning" : "notice",
                        text = f.Text,
                        modules = f.Modules.Select(id => nameOf.GetValueOrDefault(id, id.ToString())),
                    }),
                    issues = report.Issues,
                },
                Writing.Json));
        }
        else
        {
            foreach (var line in MixWords.Lines(report)) output.WriteLine(line);
        }

        return report.Warned ? Exit.Problems : Exit.Ok;
    }

    /// <summary>A level JSON can carry: null for negative infinity, which is silence.</summary>
    private static double? Finite(double level) => double.IsFinite(level) ? Math.Round(level, 2) : null;
}
