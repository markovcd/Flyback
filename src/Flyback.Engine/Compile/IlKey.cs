namespace Flyback.Engine.Compile;

/// <summary>What built code is kept under: the program's shape, and which of its parts were built.</summary>
internal readonly record struct IlKey(IlShape Shape, IlParts Parts);