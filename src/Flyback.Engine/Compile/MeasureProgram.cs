using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// A program whose result is every measured output socket side by side, in
/// <see cref="Sockets"/> order.
/// </summary>
public sealed record MeasureProgram(
    CompiledPatch Program,
    IReadOnlyList<MeasuredSocket> Sockets,
    IReadOnlyList<CompileIssue> Issues);
