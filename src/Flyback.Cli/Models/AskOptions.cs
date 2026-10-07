namespace Flyback.Cli.Models;

/// <summary>What <c>ask</c> was told, apart from the patch it is about.</summary>
/// <param name="Message">What to ask, or null to read it from standard input or, at a terminal, to keep asking.</param>
/// <param name="Provider">Which assistant, by id; null for the one the settings are on.</param>
/// <param name="Set">Provider settings for this run alone, as <c>key=value</c>.</param>
/// <param name="Fresh">Start a new conversation rather than carry on the one saved with the patch.</param>
/// <param name="Json">One JSON object a line, for each thing that happens.</param>
/// <param name="Seen">Where the pictures it looked at and the sounds it heard are written, or null for nowhere.</param>
/// <param name="Briefing">Print the briefing the assistant is handed.</param>
/// <param name="Context">How many tokens a request may send, or null for the settings' limit.</param>
/// <param name="Expand">Print the message written out in full, and build nothing.</param>
internal sealed record AskOptions(
    string? Message,
    string? Provider,
    IReadOnlyList<string> Set,
    bool Fresh,
    bool Json,
    DirectoryInfo? Seen,
    bool Briefing,
    int? Context,
    bool Expand = false);
