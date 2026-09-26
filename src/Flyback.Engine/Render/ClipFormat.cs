namespace Flyback.Core.Render;

/// <summary>
/// One way a clip can be written: a container, what goes in it, and whether that
/// is something this program encodes or something ffmpeg does.
/// </summary>
/// <param name="Id">What a settings file and the command line call this format. Never translated, never reused.</param>
/// <param name="Label">What the settings window shows.</param>
/// <param name="Extension">Including the dot, and the only thing that decides a format from a file name.</param>
/// <param name="HasPicture">False for the sound-only formats.</param>
/// <param name="Picture">
/// ffmpeg's video stream arguments, or empty where Flyback writes the picture.
/// <c>{crf}</c> is the quality, mapped by <see cref="Crf"/>.
/// </param>
/// <param name="Sound">ffmpeg's audio stream arguments, or empty where Flyback writes the sound.</param>
/// <param name="Container">ffmpeg's file-level arguments, given to whichever pass writes the finished file.</param>
public sealed record ClipFormat(
    string Id,
    string Label,
    string Extension,
    bool HasPicture,
    string Picture = "",
    string Sound = "",
    string Container = "")
{
    /// <summary>
    /// Whether writing this needs ffmpeg on the machine. The two formats written
    /// here need nothing, which is what keeps ADR-0019's promise that a clip can
    /// be written anywhere the program runs.
    /// </summary>
    public bool NeedsFfmpeg => Picture.Length > 0 || Sound.Length > 0;

    /// <summary>
    /// The quality 1 to 100 as a constant rate factor, which is what every codec
    /// here that takes a quality takes. Lower is better, opposite to the way a
    /// JPEG quality runs, so the scale is turned over on the way through.
    /// </summary>
    /// <remarks>
    /// Over 14 to 40 rather than the full 0 to 51: the ends of that range are a
    /// file nobody can afford and a picture nobody wants, and neither is worth a
    /// third of the slider. The anchors are 85 → 18, which is the quality a
    /// person means by "good", 50 → 27, which is watchable, and 100 → 14, which
    /// is as near lossless as this is worth asking for.
    /// </remarks>
    public static int Crf(int quality) => (int)Math.Round(40d - 26d * (Math.Clamp(quality, 1, 100) - 1) / 99d);

    /// <summary>The video stream's arguments with the quality filled in.</summary>
    public string PictureArguments(int quality) =>
        Picture.Replace("{crf}", Crf(quality).ToString());
}