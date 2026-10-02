namespace Flyback.Engine.Compile;

/// <summary>One output socket a measure program reads, and where its value lands in the program's result.</summary>
/// <param name="Node">The module.</param>
/// <param name="Port">Which of its outputs.</param>
/// <param name="Offset">Its first register, counted from <see cref="CompiledPatch.OutputBase"/>.</param>
/// <param name="Width">1 for a number, 3 for a color.</param>
public sealed record MeasuredSocket(Guid Node, int Port, int Offset, int Width);
