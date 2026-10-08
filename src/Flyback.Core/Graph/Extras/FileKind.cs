namespace Flyback.Core.Graph.Extras;

/// <summary>How a person picks a file of one kind, and what is said of it.</summary>
/// <param name="Label">The panel row's caption.</param>
/// <param name="Choose">The picker's title.</param>
/// <param name="Described">What the picker's filter calls the files, and what the assistant is told to find.</param>
/// <param name="Patterns">The picker's file patterns.</param>
/// <param name="MimeTypes">The picker's media types.</param>
/// <param name="Called">What a report names one by.</param>
/// <param name="Unchosen">What a report says while none is chosen.</param>
/// <param name="Picture">Looked for in the picture folder rather than the sound folder.</param>
public sealed record FileKind(
    string Label,
    string Choose,
    string Described,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> MimeTypes,
    string Called,
    string Unchosen,
    bool Picture = false);
