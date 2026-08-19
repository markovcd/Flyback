using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Cli;

/// <summary>One thing the compiler had to say, as the CLI writes it out.</summary>
/// <param name="Module">
/// The module it is about, by name rather than by id: a Guid is what the file
/// says and not what a person can find on a canvas.
/// </param>
internal sealed record Complaint(string Severity, string? Module, string Message);

/// <summary>
/// Compiles a patch for both sinks and says what is wrong with it.
/// </summary>
/// <remarks>
/// This is the command that makes a patch a thing continuous integration can
/// have an opinion about, which is why the exit code carries the answer and the
/// text is only for people. Both sinks are compiled because each walks back from
/// its own socket and neither sees what the other reaches — a patch built for
/// the ear can be broken in ways the picture's compilation never visits.
/// </remarks>
internal static class CheckCommand
{
    public static int Run(Patch patch, string name, bool json, TextWriter output, TextWriter error)
    {
        var video = patch.CompileForVideo();
        var audio = patch.CompileForAudio();

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

        // Warnings are things worth saying about a patch somebody meant. Only an
        // error is a patch that does not mean what it says.
        return errors > 0 ? Exit.Problems : Exit.Ok;
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

