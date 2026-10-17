using Flyback.Plugins.Assist;

namespace Flyback.Cli.Models;

/// <summary>What one <c>flyback-cli tool</c> call did.</summary>
/// <param name="Outcome">What the tool answered.</param>
/// <param name="Wrote">Whether it changed the patch, and so wrote the file and kept an edit.</param>
/// <param name="Step">How many edits stand after it, as <c>tool history</c> numbers them.</param>
internal sealed record ToolRan(ToolOutcome Outcome, bool Wrote, int Step);
