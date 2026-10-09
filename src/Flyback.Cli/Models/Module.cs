namespace Flyback.Cli.Models;

/// <summary>One module of the installed catalog, as this command writes it out.</summary>
internal sealed record Module(
    string TypeId,
    string Name,
    string Category,
    string Provider,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs);
