using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;
using Flyback.Gpu;
using Flyback.Plugins.Hosting;

namespace Flyback.Web;

/// <summary>
/// What <c>main.js</c> calls: open a patch, fill the sound queue, draw a frame, say
/// how it is going. Every call answers with a string a script can read, and a failure
/// is that string rather than an exception thrown across the boundary.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class WebExports
{
    private static readonly PluginCatalog Plugins = Load();

    private static readonly WebGl Gl = new();

    private static WebPlayer? player;

    /// <summary>Where <see cref="Hear"/> leaves its samples, pinned so the page can read them in place.</summary>
    private static float[] samples = [];
    private static GCHandle pinned;

    /// <summary>Where <see cref="Still"/> leaves a frame, pinned for the same reason.</summary>
    private static byte[] still = [];
    private static GCHandle stillPinned;

    private static PluginCatalog Load()
    {
        var catalog = PluginHost.LoadTypes(
            typeof(Flyback.Plugins.Easy.EasyPlugin),
            typeof(Flyback.Plugins.Effects.EffectsPlugin),
            typeof(Flyback.Plugins.Figures.FiguresPlugin),
            typeof(Flyback.Plugins.Fractals.FractalsPlugin),
            typeof(Flyback.Plugins.Mastering.MasteringPlugin),
            typeof(Flyback.Plugins.Picture.PicturePlugin),
            typeof(Flyback.Plugins.Voice.VoicePlugin));

        NodeCatalog.Install(catalog.Modules);

        return catalog;
    }

    /// <summary>Every shipped preset's name, one a line.</summary>
    [JSExport]
    public static string Presets() => string.Join('\n', Plugins.Presets.Select(p => p.Name));

    /// <summary>Opens a shipped preset. Null on success, or why not.</summary>
    [JSExport]
    public static string? OpenPreset(string name, int width, int height)
    {
        var wanted = Plugins.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (wanted is null) return $"No preset is called '{name}'.";

        return Open(() =>
        {
            var built = wanted.Build(Plugins.Modules);
            if (wanted.Files is not { } files) return new Opened(built, new SampleLibrary(), new ImageLibrary());

            var within = new BundleFiles(files());
            return new Opened(built, within, within);
        }, width, height);
    }

    /// <summary>Opens a patch file's bytes: a bundle, a document or a patch written as text. Null on success, or why not.</summary>
    [JSExport]
    public static string? OpenFile(string name, byte[] bytes, int width, int height) => Open(() =>
    {
        var extension = Path.GetExtension(name);

        if (string.Equals(extension, PatchBundle.Extension, StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream(bytes);
            var bundle = PatchBundle.Read(stream, Plugins.Modules);
            var files = BundleFiles.Of(bundle);

            return new Opened(bundle.Patch, files, files);
        }

        var text = Encoding.UTF8.GetString(bytes);

        if (string.Equals(extension, $".{PatchLanguage.FileExtension}", StringComparison.OrdinalIgnoreCase))
        {
            var built = PatchLanguage.Build(text, Plugins.Modules);
            if (!built.Ok) throw new InvalidDataException(string.Join('\n', built.Issues));

            return new Opened(built.Patch, new SampleLibrary(), new ImageLibrary());
        }

        var load = PatchIO.Read(text, Plugins.Modules);
        if (load.TooNew) throw new InvalidDataException(load.Detail);

        return new Opened(load.Patch, new SampleLibrary(), new ImageLibrary());
    }, width, height);

    private static string? Open(Func<Opened> open, int width, int height)
    {
        try
        {
            player = new WebPlayer(open(), width, height);
            return null;
        }
        catch (Exception ex)
        {
            player = null;
            return ex.Message;
        }
    }

    /// <summary>
    /// Renders the next <paramref name="frames"/> frames of sound, interleaved stereo,
    /// and answers the address they start at in the runtime's memory.
    /// </summary>
    [JSExport]
    public static int Hear(int frames)
    {
        if (samples.Length < frames * 2)
        {
            if (pinned.IsAllocated) pinned.Free();

            samples = new float[frames * 2];
            pinned = GCHandle.Alloc(samples, GCHandleType.Pinned);
        }

        var span = samples.AsSpan(0, frames * 2);

        if (player is null) span.Clear();
        else player.Hear(span);

        return (int)pinned.AddrOfPinnedObject();
    }

    /// <summary>Draws the frame at <paramref name="time"/> into a canvas of that size. Null on success, or why not.</summary>
    [JSExport]
    public static string? Draw(double time, int width, int height)
    {
        if (player is null) return null;

        try
        {
            return player.Draw(Gl, time, new SurfaceSize(width, height));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Reads back the frame at <paramref name="time"/> at the patch's resolution, and
    /// answers the address of its RGBA bytes, bottom row first; zero when there is none.
    /// </summary>
    [JSExport]
    public static int Still(double time)
    {
        if (player is null) return 0;

        var bytes = player.Resolution.Width * player.Resolution.Height * 4;

        if (still.Length != bytes)
        {
            if (stillPinned.IsAllocated) stillPinned.Free();

            still = new byte[bytes];
            stillPinned = GCHandle.Alloc(still, GCHandleType.Pinned);
        }

        return player.Still(Gl, time, still) is null ? (int)stillPinned.AddrOfPinnedObject() : 0;
    }

    /// <summary>Whether the picture's shader is still being built, which is when every frame has to be drawn.</summary>
    [JSExport]
    public static bool Linking() => player?.Linking ?? false;

    /// <summary>Back to <paramref name="seconds"/>, sound and picture both.</summary>
    [JSExport]
    public static void Seek(double seconds) => player?.SeekTo(seconds);

    /// <summary>Seconds of sound rendered for each second spent, over <paramref name="seconds"/> of the open patch.</summary>
    [JSExport]
    public static double Measure(double seconds) => player?.Measure(seconds) ?? 0;

    /// <summary>The open patch as JSON: its size, cost, how far the sound has got and how fast it renders.</summary>
    [JSExport]
    public static string Status()
    {
        if (player is null) return "{\"open\":false}";

        return new JsonObject
        {
            ["open"] = true,
            ["length"] = player.Length,
            ["soundOps"] = player.SoundOps,
            ["pictureOps"] = player.PictureOps,
            ["sampleRate"] = player.SampleRate,
            ["rendered"] = player.Time,
            ["speed"] = Math.Round(player.Speed, 3),
            ["width"] = player.Resolution.Width,
            ["height"] = player.Resolution.Height,
            ["linking"] = player.Linking,
            ["eightBitFeedback"] = player.EightBitFeedback,
        }.ToJsonString();
    }
}
