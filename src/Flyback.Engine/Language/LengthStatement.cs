namespace Flyback.Core.Language;

/// <summary><c>length 2:30.50</c>: how long the patch plays for, in seconds.</summary>
public sealed record LengthStatement(double Seconds, int Line, int Column) : Statement(Line, Column);