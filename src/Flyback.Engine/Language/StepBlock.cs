using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// What a step block expands to: a flat list of <see cref="Step"/>, and nothing else.
/// </summary>
/// <param name="Steps">The tune, already flattened.</param>
/// <param name="RateDivisor">
/// What the sequencer's rate must be divided by for the pattern to take the same time
/// it would have. Only <c>&lt;a b&gt;</c> moves it: alternation is unrolled into a
/// longer list, which has to be read more slowly to sound the same.
/// </param>
public readonly record struct StepBlock(IReadOnlyList<Step> Steps, int RateDivisor);