namespace Flyback.Engine.Language.Ast;

/// <summary>One <c>out name = value "help"</c> line ending a module's body: an output with its name and help.</summary>
/// <param name="Help">What it gives, or empty where the line says nothing.</param>
public sealed record NamedOutput(string Name, string Help, int Line, int Column);
