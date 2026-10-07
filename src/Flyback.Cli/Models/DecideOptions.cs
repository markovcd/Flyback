namespace Flyback.Cli.Models;

/// <summary>What to ask a decision model, or what to do about one instead.</summary>
/// <param name="State">The text the questions are about; <c>-</c> reads it from standard input.</param>
/// <param name="Ask">A file of questions in the System One format.</param>
/// <param name="Model">Which model, by id, or null for the chosen one.</param>
/// <param name="YesNo">One yes-no question, instead of a file.</param>
/// <param name="Choice">One choice question, its options in <paramref name="Options"/>.</param>
/// <param name="Options">A choice's options, each <c>label=description</c>.</param>
/// <param name="Score">One score question, its levels in <paramref name="Levels"/>, lowest first.</param>
/// <param name="Status">List the models and whether each can answer, and ask nothing.</param>
/// <param name="Prepare">Download what the model needs, and ask nothing.</param>
/// <param name="Yes">Download without the question.</param>
internal sealed record DecideOptions(
    string? State,
    FileInfo? Ask,
    string? Model,
    string? YesNo,
    string? Choice,
    IReadOnlyList<string> Options,
    string? Score,
    IReadOnlyList<string> Levels,
    bool Status,
    bool Prepare,
    bool Yes,
    bool Json);
