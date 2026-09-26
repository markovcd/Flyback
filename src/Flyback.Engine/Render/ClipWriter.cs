namespace Flyback.Core.Render;

/// <summary>Opens the writer a <see cref="ClipTarget"/> asks for.</summary>
public static class ClipWriter
{
    /// <summary>
    /// The one place a format becomes an encoder. Everything that writes a clip
    /// comes through here, so the shell's take and the command line's render
    /// cannot end up supporting different lists.
    /// </summary>
    public static IClipWriter Open(ClipTarget target)
    {
        if (target.Format.NeedsFfmpeg)
        {
            var ffmpeg = target.Ffmpeg
                ?? throw new InvalidOperationException($"{target.Format.Label} needs ffmpeg, and none was found.");

            return new FfmpegClipWriter(target, ffmpeg);
        }

        // Left to the writers below to dispose along with themselves, so that a
        // caller holds one thing per clip whichever format it is.
        var file = File.Create(target.Path);

        try
        {
            return target.Format.HasPicture
                ? new AviClipWriter(file, target, owned: true)
                : new WavClipWriter(file, target, owned: true);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }
}
