using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Cli.Common;
using Flyback.Cli.Models;

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
    public static int Run(
        Patch patch,
        string name,
        bool json,
        TextWriter output,
        TextWriter error,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        bool strict = false)
    {
        var video = patch.CompileForVideo(samples: samples, pictures: pictures);
        var audio = patch.CompileForAudio(samples: samples);

        // Deduplicated across the two, the way the window's status line does it:
        // a module both sinks reach complains once about the same thing, and
        // hearing it twice would say something untrue about how many there are.
        var complaints = video.Issues
            .Concat(audio.Issues)
            .DistinctBy(i => (i.NodeId, i.Message))
            .Select(i => new Complaint(
                i.Severity == IssueSeverity.Error ? "error" : "warning",
                NameOf(patch, i.NodeId),
                i.Message))
            .ToArray();

        var errors = complaints.Count(c => c.Severity == "error");

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new { patch = name, errors, warnings = complaints.Length - errors, issues = complaints },
                Writing.Json));
        }
        else
        {
            Write(name, complaints, errors, output);
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
            var complaints = issues
                .Select(i => new Complaint("error", null, i.Message, i.Line, i.Column, i.Code))
                .ToArray();

            output.WriteLine(JsonSerializer.Serialize(
                new { patch = name, errors = complaints.Length, warnings = 0, issues = complaints },
                Writing.Json));
        }
        else
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {name}: this patch does not read.");

            foreach (var issue in issues) error.WriteLine($"    {name}:{issue.Line}:{issue.Column}: {issue.Message}");
        }

        return Exit.Problems;
    }

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
            var about = complaint.Module is null ? string.Empty : $"{complaint.Module}: ";
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
