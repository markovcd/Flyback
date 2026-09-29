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
/// What the page and its sound worker call: open a patch, fill the sound queue, draw a
/// frame, say how it is going. Every call answers with a string a script can read, and
/// a failure is that string rather than an exception thrown across the boundary.
/// </summary>
/// <remarks>
/// The page opens the picture and the worker opens the sound, each in a runtime of its
/// own; <c>hear.mjs</c> opens the sound alone. A call about the half that is not open
/// here does nothing.
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class WebExports
{
    private const string SoundPart = "sound";
    private const string PicturePart = "picture";

    private static readonly PluginCatalog Plugins = Load();

    private static readonly WebGl Gl = new();

    private static WebSound? sound;
    private static WebPicture? picture;

    /// <summary>What the open patch says it is for, and its panel's knobs.</summary>
    private static string? description;
    private static IReadOnlyList<PatchControl> knobs = [];

    /// <summary>The keys under the page's hands, laid out as the open patch asks.</summary>
    private static readonly ComputerKeyboard Typing = new();

    /// <summary>Where <see cref="Hear"/> leaves its samples, pinned so the page can read them in place.</summary>
    private static readonly Pinned<float> Samples = new();

    /// <summary>Where <see cref="Listen"/> packs what the picture knows of the sound, and <see cref="Apply"/> reads it.</summary>
    private static readonly Pinned<float> State = new();

    /// <summary>Where <see cref="Still"/> leaves a frame.</summary>
    private static readonly Pinned<byte> Frame = new();

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

    /// <summary>
    /// The shipped presets as JSON, each with the heading of its run, in the editor's
    /// order. The blank canvas is left out, having nothing to play.
    /// </summary>
    [JSExport]
    public static string Presets() => new JsonArray([
        .. Plugins.Presets
            .Where(p => p.Kind != PresetKind.Blank)
            .OrderBy(p => p.Kind)
            .Select(p => new JsonObject { ["name"] = p.Name, ["heading"] = PresetKinds.Heading(p.Kind) }),
    ]).ToJsonString();

    /// <summary>
    /// A shipped preset built once and packed as a bundle, with the files it plays, for
    /// <see cref="OpenFile"/>; empty for a name no preset has.
    /// </summary>
    /// <remarks>
    /// Building a preset gives its modules and knobs new ids every time, so the page and
    /// its sound worker open these same bytes, and agree on every name the two share.
    /// </remarks>
    [JSExport]
    public static byte[] Pack(string name)
    {
        var wanted = Plugins.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (wanted is null) return [];

        var built = wanted.Build(Plugins.Modules);
        built.Description ??= wanted.Description.Length > 0 ? wanted.Description : null;

        var files = wanted.Files?.Invoke();

        using var packed = new MemoryStream();
        PatchBundle.Write(packed, built, path => files?.GetValueOrDefault(path), Plugins.Modules);

        return packed.ToArray();
    }

    /// <summary>
    /// Opens <paramref name="part"/>, sound or picture, of a patch file's bytes: a bundle,
    /// a document or a patch written as text. Null on success, or why not.
    /// </summary>
    [JSExport]
    public static string? OpenFile(string name, byte[] bytes, int width, int height, string part) => Open(() =>
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
    }, width, height, part);

    private static string? Open(Func<Opened> open, int width, int height, string part)
    {
        if (part is not (SoundPart or PicturePart)) return $"There is no part called '{part}': ask for '{SoundPart}' or '{PicturePart}'.";

        sound?.Dispose();
        sound = null;
        picture = null;
        description = null;
        knobs = [];

        try
        {
            var opened = open();

            if (part == SoundPart) sound = new WebSound(opened, width, height);
            else picture = new WebPicture(opened, width, height);

            description = opened.Patch.Description;
            knobs = [.. opened.Patch.Controls ?? []];
            Typing.Scale = opened.Patch.Keyboard;

            return null;
        }
        catch (Exception ex)
        {
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
        var span = Samples.Take(frames * 2);

        if (sound is null) span.Clear();
        else sound.Hear(span);

        return Samples.Address;
    }

    /// <summary>
    /// Packs what the picture knows of the sound, its Meters' readings, and answers
    /// the address it starts at; <c>stateLength</c> in <see cref="Status"/> says how long it is.
    /// </summary>
    [JSExport]
    public static int Listen()
    {
        if (sound is null) return 0;

        sound.Listen(State.Take(sound.StateLength));

        return State.Address;
    }

    /// <summary>
    /// Where to write what the sound's worker packed with <see cref="Listen"/>, before
    /// calling <see cref="Apply"/>.
    /// </summary>
    [JSExport]
    public static int Heard()
    {
        if (picture is null) return 0;

        State.Take(picture.StateLength);

        return State.Address;
    }

    /// <summary>Hands the picture what was written where <see cref="Heard"/> said.</summary>
    [JSExport]
    public static void Apply() => picture?.Apply(State.Take(picture.StateLength));

    /// <summary>Draws the frame at <paramref name="time"/> into a canvas of that size. Null on success, or why not.</summary>
    [JSExport]
    public static string? Draw(double time, int width, int height)
    {
        if (picture is null) return null;

        try
        {
            return picture.Draw(Gl, time, new SurfaceSize(width, height));
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
        if (picture is null) return 0;

        var rgba = Frame.Take(picture.Resolution.Width * picture.Resolution.Height * 4);

        return picture.Still(Gl, time, rgba) is null ? Frame.Address : 0;
    }

    /// <summary>Whether the picture's shader is still being built, which is when every frame has to be drawn.</summary>
    [JSExport]
    public static bool Linking() => picture?.Linking ?? false;

    /// <summary>Back to <paramref name="seconds"/>: the sound from there, the picture from what it remembers emptied.</summary>
    [JSExport]
    public static void Seek(double seconds)
    {
        sound?.SeekTo(seconds);
        picture?.Rewind();
    }

    /// <summary>Seconds of sound rendered for each second spent, over <paramref name="seconds"/> of the open patch.</summary>
    [JSExport]
    public static double Measure(double seconds) => sound?.Measure(seconds) ?? 0;

    /// <summary>
    /// The panel's knobs as JSON: each one's key, name, where it rests and where the open
    /// half has it turned to, 0 to 1.
    /// </summary>
    [JSExport]
    public static string Knobs() => new JsonArray([
        .. knobs.Select(k => new JsonObject
        {
            ["key"] = k.Key,
            ["name"] = k.Name,
            ["rest"] = k.Value,
            ["value"] = sound?.Reading(k.Key) ?? picture?.Reading(k.Key) ?? k.Value,
        }),
    ]).ToJsonString();

    /// <summary>Turns the knob called <paramref name="key"/> to <paramref name="value"/>, 0 to 1.</summary>
    [JSExport]
    public static void Turn(string key, double value)
    {
        var turned = (float)Math.Clamp(value, 0, 1);

        sound?.Turn(key, turned);
        picture?.Turn(key, turned);
    }

    /// <summary>The note the key named <paramref name="key"/> plays, as a browser names it (<c>KeyZ</c>), or -1 for none.</summary>
    [JSExport]
    public static int KeyNote(string key) => Typing.Note(key) ?? -1;

    /// <summary>Moves the computer keyboard <paramref name="octaves"/> up or down, and says where it is now.</summary>
    [JSExport]
    public static string Shift(int octaves)
    {
        Typing.Octave += octaves;

        return Typing.Described;
    }

    /// <summary>A note on the computer keyboard pressed or let go, for the sound's half.</summary>
    [JSExport]
    public static void Strike(int note, bool down) => sound?.Strike(note, down);

    /// <summary>Every computer keyboard note let go.</summary>
    [JSExport]
    public static void Release() => sound?.Release();

    /// <summary>The open half as JSON: its size, cost, how far the sound has got and how fast it renders.</summary>
    [JSExport]
    public static string Status()
    {
        var status = new JsonObject { ["open"] = sound is not null || picture is not null };

        if (sound is not null)
        {
            status["length"] = sound.Length;
            status["soundOps"] = sound.SoundOps;
            status["sampleRate"] = sound.SampleRate;
            status["rendered"] = sound.Time;
            status["speed"] = Math.Round(sound.Speed, 3);
            status["soundBackend"] = sound.Interpreted is null ? "javascript" : "interpreter";
            status["interpreted"] = sound.Interpreted;
            status["stateLength"] = sound.StateLength;
            status["played"] = sound.Played;
            status["sounding"] = new JsonArray([.. sound.Sounding.Select(note => JsonValue.Create(note))]);
        }

        if (picture is not null)
        {
            status["length"] = picture.Length;
            status["pictureOps"] = picture.PictureOps;
            status["width"] = picture.Resolution.Width;
            status["height"] = picture.Resolution.Height;
            status["linking"] = picture.Linking;
            status["eightBitFeedback"] = picture.EightBitFeedback;
            status["stateLength"] = picture.StateLength;
            status["keyboard"] = Typing.Described;
        }

        if (status["open"]!.GetValue<bool>()) status["description"] = description;

        return status.ToJsonString();
    }

    /// <summary>An array pinned where it is, so the page reads and writes it in place, grown as asked.</summary>
    private sealed class Pinned<T> where T : unmanaged
    {
        private T[] array = [];
        private GCHandle handle;

        public int Address => (int)handle.AddrOfPinnedObject();

        public Span<T> Take(int length)
        {
            if (array.Length < length || !handle.IsAllocated)
            {
                if (handle.IsAllocated) handle.Free();

                array = new T[Math.Max(length, 1)];
                handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            }

            return array.AsSpan(0, length);
        }
    }
}
