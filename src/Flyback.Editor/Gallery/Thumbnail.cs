namespace Flyback.App.Gallery;

/// <summary>
/// What a preset's tile shows in the place of a picture, or the picture itself.
/// </summary>
/// <param name="Pixels">
/// A BGRA frame <see cref="PresetThumbnails.Width"/> by <see cref="PresetThumbnails.Height"/>,
/// or null for a preset that has none to show.
/// </param>
/// <param name="Words">What the tile says instead. Empty when there are pixels, or nothing to say.</param>
/// <param name="Description">What the patch says it is for, and null where it says nothing or would not open.</param>
/// <param name="Author">Who the patch says made it, and null where it says nobody or would not open.</param>
/// <param name="Tags">What the patch is tagged, and null where it has no tags or would not open.</param>
internal sealed record Thumbnail(
    byte[]? Pixels, string Words, string? Description = null, string? Author = null, IReadOnlyList<string>? Tags = null)
{
    /// <summary>
    /// A patch that is heard and never seen, which has no frame to take. Shown as a
    /// speaker, the words being what it says when pointed at.
    /// </summary>
    public static Thumbnail SoundOnly { get; } = new(null, "Sound only");

    /// <summary>
    /// A patch with nothing wired to either half of the Output yet. Left bare, its
    /// name and description being what says so.
    /// </summary>
    public static Thumbnail Nothing { get; } = new(null, "");

    /// <summary>A patch that would not build or compile, so there is no frame to show.</summary>
    public static Thumbnail Unavailable { get; } = new(null, "No preview");
}