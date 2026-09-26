namespace Flyback.Core.Graph;

/// <summary>
/// What opening a path came to: the patch to play, or null when there is none,
/// and every complaint made on the way, one line each.
/// </summary>
/// <remarks>
/// Complaints and a patch can come together: one short of a plugin still has
/// something to show.
/// </remarks>
public readonly record struct PatchOpen(Opened? Patch, IReadOnlyList<string> Problems);