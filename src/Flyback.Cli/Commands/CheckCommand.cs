using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Compiles a patch for both sinks and says what is wrong with it.
/// </summary>
/// <remarks>
/// The command that makes a patch a thing continuous integration can have an opinion
/// about, which is why the exit code carries the answer and the text is only for
/// people. Both sinks, because each walks back from its own socket: a patch built for
/// the ear can be broken in ways the picture's compilation never visits.
/// </remarks>
internal static class CheckCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to read: a document, a bundle, or one written as text. "
                + $"The extension decides which — .{PatchIO.FileExtension}, "
                + $"{PatchBundle.Extension} or .{PatchLanguage.FileExtension}. Left out, give --preset instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "A shipped preset, by name, in place of a file.",
        };

        var presets = new Option<bool>("--presets")
        {
            Description = "List what --preset would accept, and stop.",
        };

        var strict = new Option<bool>("--strict")
        {
            Description = "Fail on warnings as well as on errors.",
        };

        var command = new Command("check", "Compile a patch and report what is wrong with it.")
        {
            patch, preset, presets, json, strict,
        };

        command.SetAction(result =>
        {
            var error = result.InvocationConfiguration.Error;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, result.InvocationConfiguration.Output, result.GetValue(json));

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to check: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            if (file is null)
            {
                return ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped
                    ? Exit.Failed
                    : Run(
                        shipped.Opened.Patch,
                        shipped.Name,
                        result.GetValue(json),
                        result.InvocationConfiguration.Output,
                        error,
                        shipped.Opened.Samples,
                        shipped.Opened.Pictures,
                        result.GetValue(strict));
            }

            var read = Patches.Sourced(file) && file.Exists ? PatchLanguage.Build(File.ReadAllText(file.FullName)) : null;

            // Text that does not read is a patch with something wrong with it, not
            // a file that could not be looked at, so it answers the way a compile
            // error does.
            if (read is { Ok: false } unread)
            {
                return Unread(
                    unread.Issues,
                    file.Name,
                    result.GetValue(json),
                    result.InvocationConfiguration.Output,
                    result.InvocationConfiguration.Error);
            }

            return Patches.Open(file, result.InvocationConfiguration.Error) is not { } opened
                ? Exit.Failed
                : Run(
                    opened.Patch,
                    file.Name,
                    result.GetValue(json),
                    result.InvocationConfiguration.Output,
                    result.InvocationConfiguration.Error,
                    opened.Samples,
                    opened.Pictures,
                    result.GetValue(strict),
                    read?.Issues);
        });

        return command;
    }

    public static int Run(
        Patch patch,
        string name,
        bool json,
        TextWriter output,
        TextWriter error,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool strict = false,
        IReadOnlyList<LanguageIssue>? read = null)
    {
        var video = patch.CompileForVideo(samples: samples, pictures: pictures);
        var audio = patch.CompileForAudio(samples: samples);

        // Deduplicated across the two, the way the window's status line does it:
        // a module both sinks reach complains once about the same thing, and
        // hearing it twice would say something untrue about how many there are.
        var complaints = (read ?? [])
            .Select(Complained)
            .Concat(video.Issues
                .Concat(audio.Issues)
                .DistinctBy(i => (i.NodeId, i.Message))
                .Select(i => new Complaint(
                    i.Severity == IssueSeverity.Error ? "error" : "warning",
                    NameOf(patch, i.NodeId),
                    i.Message)))
            .ToArray();

        var errors = complaints.Count(c => c.Severity == "error");

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new { patch = name, plugins = Loaded(), errors, warnings = complaints.Length - errors, issues = complaints },
                Writing.Json));
        }
        else
        {
            Write(name, complaints, errors, output);
            output.WriteLine(Loading());
        }

        // Warnings are things worth saying about a patch somebody meant, so only
        // an error is a patch that does not mean what it says — unless a build has
        // decided otherwise, which is what --strict is.
        return errors > 0 || (strict && complaints.Length > 0) ? Exit.Problems : Exit.Ok;
    }

    /// <summary>Says what is wrong with a text patch that does not build.</summary>
    public static int Unread(
        IReadOnlyList<LanguageIssue> issues,
        string name,
        bool json,
        TextWriter output,
        TextWriter error)
    {
        if (json)
        {
            var complaints = issues.Select(Complained).ToArray();
            var errors = complaints.Count(c => c.Severity == "error");

            output.WriteLine(JsonSerializer.Serialize(
                new { patch = name, plugins = Loaded(), errors, warnings = complaints.Length - errors, issues = complaints },
                Writing.Json));
        }
        else
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {name}: this patch does not read.");

            foreach (var issue in issues) error.WriteLine($"    {name}:{issue}");

            error.WriteLine(Loading());
        }

        return Exit.Problems;
    }

    /// <summary>The plugins this run had loaded, which decide what a module's short name means.</summary>
    private static string[] Loaded() => [.. NodeCatalog.Current.Providers
        .Where(p => p != NodeCatalog.BuiltInProvider)
        .Select(p => p.Id)
        .Order(StringComparer.Ordinal)];

    private static string Loading() => Loaded() is [_, ..] ids
        ? $"plugins: {string.Join(", ", ids)}."
        : "plugins: none.";

    private static Complaint Complained(LanguageIssue issue) =>
        new(issue.IsError ? "error" : "warning", null, issue.Message, issue.Line, issue.Column, issue.Code);

    private static void Write(string name, Complaint[] complaints, int errors, TextWriter output)
    {
        if (complaints.Length == 0)
        {
            output.WriteLine($"{name}: nothing to report.");
            return;
        }

        output.WriteLine(name);

        foreach (var complaint in complaints)
        {
            var about = complaint.Module is not null ? $"{complaint.Module}: "
                : complaint.Line is { } line ? $"{line}:{complaint.Column}: "
                : string.Empty;
            output.WriteLine($"  {complaint.Severity,-7}  {about}{complaint.Message}");
        }

        var warnings = complaints.Length - errors;

        output.WriteLine(errors switch
        {
            0 => $"{Writing.Count(warnings, "warning")}.",
            _ => $"{Writing.Count(errors, "error")}, {Writing.Count(warnings, "warning")}.",
        });
    }

    /// <summary>
    /// What to call the module an issue is about. Null for an issue about the
    /// patch as a whole, and for a node that is named but no longer there.
    /// </summary>
    private static string? NameOf(Patch patch, Guid? node) =>
        node is { } id && patch.Find(id) is { } instance
            ? NodeCatalog.Get(instance.TypeId)?.Name ?? instance.TypeId
            : null;
}
