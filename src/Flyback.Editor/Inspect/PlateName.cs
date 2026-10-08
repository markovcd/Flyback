namespace Flyback.Editor.Inspect;

/// <summary>How the block a plate stands for is renamed, for whatever puts a box where its name is.</summary>
/// <param name="Held">The name it has of its own, or null.</param>
/// <param name="Fallback">What it is called without one.</param>
/// <param name="Limit">How long a name may be.</param>
/// <param name="Rename">Gives it the name typed, or takes its own away for an empty one.</param>
/// <param name="Changed">Records the rename as an edit.</param>
internal sealed record PlateName(Func<string?> Held, string Fallback, int Limit, Action<string?> Rename, Action Changed);
