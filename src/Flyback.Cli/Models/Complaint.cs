using Flyback.Engine.Language;

namespace Flyback.Cli.Models;

/// <summary>One thing the compiler had to say, as the CLI writes it out.</summary>
/// <param name="Module">
/// The module it is about, by name rather than by id: a Guid is what the file
/// says and not what a person can find on a canvas.
/// </param>
/// <param name="Line">Where in a text patch, for a complaint about the text.</param>
/// <param name="Column">Where on that line.</param>
/// <param name="Code">What kind of mistake the text holds, one of <see cref="IssueCode"/>.</param>
internal sealed record Complaint(
    string Severity,
    string? Module,
    string Message,
    int? Line = null,
    int? Column = null,
    string? Code = null);