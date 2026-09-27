namespace Flyback.Cli.Models;

/// <summary>One input of a described module: what it is set to unwired and what it takes.</summary>
/// <param name="Min">Null where the socket declares no range.</param>
/// <param name="Display">How the text writes a value for it: a number, a note, a duration or a whole number.</param>
/// <param name="WiredOnly">True where the socket has no knob and does nothing until wired.</param>
/// <param name="Help">What the socket is for, and empty only for a plugin that says nothing.</param>
internal sealed record Input(
    int Index,
    string Name,
    string Kind,
    string Display,
    float Default,
    float? Min,
    float? Max,
    bool WiredOnly,
    string Help);