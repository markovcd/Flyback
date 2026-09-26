namespace Flyback.Core.Language;

/// <summary>A pipeline standing on its own, which is the only statement with an effect.</summary>
public sealed record PipelineStatement(Expr Value, int Line, int Column) : Statement(Line, Column);