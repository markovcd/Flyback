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

/// <summary>
/// Every format a clip can be written as. Two of them are this program's own and
/// the rest are ffmpeg's — see ADR-0089 for why both lists exist rather than one.
/// </summary>
/// <remarks>
/// The ffmpeg arguments are held here rather than built where they are used, so
/// that adding a format is one row in one file and the shell's picker, the command
/// line's <c>--format</c> and the extension of an output file all learn it at once.
/// </remarks>
public static class ClipFormats
{
    /// <summary>
    /// What ADR-0036 wrote by hand, and still the answer on a machine with no
    /// ffmpeg. Every frame a full JPEG, which makes it both the largest of these
    /// and — since compressing a frame badly here costs more than compressing it
    /// well there — the slowest. Twenty-five times H.264's size, measured on the
    /// clip ADR-0089 tabulates.
    /// </summary>
    public static readonly ClipFormat MotionJpegAvi = new(
        "avi", "AVI, Motion JPEG", ".avi", HasPicture: true);

    /// <summary>The file everybody wants, and the default wherever ffmpeg is to be had.</summary>
    public static readonly ClipFormat H264Mp4 = new(
        "mp4", "MP4, H.264", ".mp4", HasPicture: true,
        Picture: "-c:v libx264 -preset medium -crf {crf} -pix_fmt yuv420p "
            + "-vf pad=ceil(iw/2)*2:ceil(ih/2)*2",
        Sound: "-c:a aac -b:a 192k",
        Container: "-movflags +faststart");

    /// <summary>About half the size of H.264 at the same quality, for a clip that has to travel.</summary>
    public static readonly ClipFormat H265Mp4 = new(
        "hevc", "MP4, H.265", ".mp4", HasPicture: true,
        Picture: "-c:v libx265 -preset medium -crf {crf} -pix_fmt yuv420p "
            + "-vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -tag:v hvc1",
        Sound: "-c:a aac -b:a 192k",
        Container: "-movflags +faststart");

    /// <summary>What goes on a web page without asking anybody's permission.</summary>
    public static readonly ClipFormat Vp9WebM = new(
        "webm", "WebM, VP9", ".webm", HasPicture: true,
        Picture: "-c:v libvpx-vp9 -crf {crf} -b:v 0 -pix_fmt yuv420p "
            + "-vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -row-mt 1",
        Sound: "-c:a libopus -b:a 160k");

    /// <summary>
    /// For a clip that is going into an editor rather than to somebody to watch.
    /// Intra-frame and 10-bit, so it costs far more than H.264 and loses nothing
    /// a second pass would need.
    /// </summary>
    public static readonly ClipFormat ProResMov = new(
        "prores", "MOV, ProRes 422 HQ", ".mov", HasPicture: true,
        Picture: "-c:v prores_ks -profile:v 3 -pix_fmt yuv422p10le -vf pad=ceil(iw/2)*2:ceil(ih/2)*2",
        Sound: "-c:a pcm_s16le");

    /// <summary>What ADR-0036 wrote by hand: the samples, and nothing done to them.</summary>
    public static readonly ClipFormat Wav = new(
        "wav", "WAV, 16-bit PCM", ".wav", HasPicture: false);

    /// <summary>
    /// The one every device made since will play. VBR at the second-highest
    /// setting — around 190 kbps — rather than a quality this takes from the
    /// picture's slider, since nobody exporting a take is choosing between a
    /// transparent MP3 and a smaller one.
    /// </summary>
    public static readonly ClipFormat Mp3 = new(
        "mp3", "MP3, ~190 kbps", ".mp3", HasPicture: false,
        Sound: "-c:a libmp3lame -q:a 2");

    /// <inheritdoc cref="Mp3"/>
    public static readonly ClipFormat Aac = new(
        "m4a", "M4A, AAC 192 kbps", ".m4a", HasPicture: false,
        Sound: "-c:a aac -b:a 192k");

    /// <summary>Lossless and about half the size of the WAV, for an archive of a take.</summary>
    public static readonly ClipFormat Flac = new(
        "flac", "FLAC, lossless", ".flac", HasPicture: false,
        Sound: "-c:a flac");

    /// <summary>The formats with a picture in them, the one written here first.</summary>
    public static readonly IReadOnlyList<ClipFormat> Pictures =
        [MotionJpegAvi, H264Mp4, H265Mp4, Vp9WebM, ProResMov];

    /// <inheritdoc cref="Pictures"/>
    public static readonly IReadOnlyList<ClipFormat> Sounds = [Wav, Mp3, Aac, Flac];

    public static readonly IReadOnlyList<ClipFormat> All = [.. Pictures, .. Sounds];

    /// <summary>The format an id names, or null for one nothing here defines.</summary>
    public static ClipFormat? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The format a file name asks for, or null for an extension none of these
    /// writes. The first row with that extension wins, which is why H.264 is
    /// listed before H.265 — <c>.mp4</c> alone means the one people mean.
    /// </summary>
    public static ClipFormat? ByExtension(string path)
    {
        var extension = Path.GetExtension(path);

        return extension.Length == 0
            ? null
            : All.FirstOrDefault(f => string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Which video format to start on with nothing saved yet: H.264 wherever
    /// there is an ffmpeg to write it with, and the one written here otherwise.
    /// </summary>
    /// <remarks>
    /// The only choice here that is a question about the machine rather than
    /// about a file. Motion JPEG always works, but it is twenty-five times the
    /// size and reaches AVI's 4 GB ceiling in about half an hour — a fallback
    /// rather than a preference. A format already saved is never put through this: see
    /// <see cref="Wanted"/>.
    /// </remarks>
    public static ClipFormat Preferred(bool ffmpeg) => ffmpeg ? H264Mp4 : MotionJpegAvi;

    /// <summary>
    /// The format a setting means, falling back to the one written here for an id
    /// this build does not define — the rule ADR-0034 puts on every setting.
    /// </summary>
    /// <param name="picture">
    /// Which list it has to come from. A sound format saved where a video one
    /// belongs is as unusable as an id nothing defines.
    /// </param>
    /// <remarks>
    /// Whether ffmpeg is actually on the machine is deliberately not asked here.
    /// A saved choice that this launch cannot honor is still the choice, and
    /// quietly rewriting it to the built-in format would lose it for good the
    /// next time anything was saved.
    /// </remarks>
    public static ClipFormat Wanted(string? id, bool picture) =>
        ById(id) is { } format && format.HasPicture == picture
            ? format
            : picture ? MotionJpegAvi : Wav;
}
