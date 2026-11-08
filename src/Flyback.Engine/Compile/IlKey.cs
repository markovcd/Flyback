namespace Flyback.Engine.Compile;

/// <summary>
/// What built code is kept under: the program's shape, which of its parts were built,
/// and the most legs its sound could be cut into.
/// </summary>
internal readonly record struct IlKey(IlShape Shape, IlParts Parts, int Legs);
