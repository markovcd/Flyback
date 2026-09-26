namespace Flyback.Core.Language;

/// <summary><c>requires flyback.picture, flyback.effects</c>: the plugins the patch cannot be built without.</summary>
public sealed record RequiresStatement(IReadOnlyList<string> Plugins, int Line, int Column) : Statement(Line, Column);