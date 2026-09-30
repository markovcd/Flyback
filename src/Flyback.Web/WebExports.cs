using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using Flyback.Core.Compile;
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

    /// <summary>Whether anything is wired into the open patch's sound.</summary>
    private static bool hasSound;

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
        var catalog = PluginHost.LoadLinked(typeof(WebExports).Assembly);

        NodeCatalog.Install(catalog.Modules);

        return catalog;
    }

    /// <summary>
    /// The shipped presets as JSON, each with the heading of its run and its description,
    /// in the editor's order. The blank canvas is left out, having nothing to play.
    /// </summary>
    [JSExport]
    public static string Presets() => new JsonArray([
        .. Plugins.Presets
            .Where(p => p.Kind != PresetKind.Blank)
            .Select(p => new JsonObject
            {
                ["name"] = p.Name,
                ["heading"] = PresetKinds.Heading(p.Kind),
                ["description"] = p.Description,
            }),
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
            if (bundle.Load is { IsComplete: false } lacking) throw Refused(lacking);

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
        if (!load.IsComplete) throw Refused(load);

        return new Opened(load.Patch, new SampleLibrary(), new ImageLibrary());
    }, width, height, part);

    /// <summary>A patch this page cannot build all of is not played in part, as the editor opens none of it.</summary>
    private static InvalidDataException Refused(PatchLoad load) =>
        new(load.TooNew ? load.Detail : $"Not opened. {load.Summary}");

    /// <summary>The files the editor's patch names, as it handed them over with <see cref="Keep"/>.</summary>
    private static readonly Dictionary<string, byte[]> Kept = new(StringComparer.OrdinalIgnoreCase);
    private static BundleFiles keptFiles = new(Kept);

    /// <summary>The Meters the editor's picture reads, measured by <see cref="Readings"/>.</summary>
    private static LiveValues watched = LiveValues.None;

    /// <summary>The Scopes and Analyzers the editor's picture draws, refilled by <see cref="Readings"/>.</summary>
    private static TapSpec[] charted = [];

    /// <summary>Keeps a file the editor's patch names, for every <see cref="Edit"/> after.</summary>
    [JSExport]
    public static void Keep(string path, byte[] bytes)
    {
        Kept[path] = bytes;
        keptFiles = new BundleFiles(Kept);
    }

    /// <summary>Lets every kept file go, before the files of another patch are kept.</summary>
    [JSExport]
    public static void Forget()
    {
        Kept.Clear();
        keptFiles = new BundleFiles(Kept);
    }

    /// <summary>
    /// The editor's patch, as text, taking over the sound from the one playing: its
    /// clock and what it remembers carry on, as the editor's engine carries them
    /// through an edit. Null on success, or why not, with the old sound still playing.
    /// </summary>
    [JSExport]
    public static string? Edit(string text, double aspect)
    {
        try
        {
            var load = PatchIO.Read(text, Plugins.Modules);
            if (load.TooNew) return load.Detail;

            var next = new WebSound(new Opened(load.Patch, keptFiles, keptFiles), (float)aspect, sound);

            sound?.Dispose();
            sound = next;
            picture = null;
            description = load.Patch.Description;
            knobs = [.. load.Patch.Controls ?? []];
            hasSound = load.Patch.Reaches().Sound;

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Says which Meters <see cref="Readings"/> measures, by the names the editor's picture
    /// reads them on, and which charts it refills, by module, window and whether each is a
    /// spectrum (1) or a trace (0). Answers how many floats the readings take.
    /// </summary>
    [JSExport]
    public static int Watch(string[] keys, string[] charts, double[] windows, int[] spectra)
    {
        watched = new LiveValues(keys);
        charted =
        [
            .. charts
                .Select((node, i) => (Ok: Guid.TryParse(node, out var id), Id: id, At: i))
                .Where(chart => chart.Ok && chart.At < windows.Length && chart.At < spectra.Length)
                .Select(chart => new TapSpec(chart.Id, (float)windows[chart.At], Traces.Buffer(), spectra[chart.At] != 0)),
        ];

        return watched.Count + charted.Sum(chart => chart.Trace.Samples.Length);
    }

    /// <summary>
    /// Measures the Meters <see cref="Watch"/> named and refills its charts, in its order,
    /// and answers the address the readings start at; zero where there are none.
    /// </summary>
    [JSExport]
    public static int Readings()
    {
        if (sound is null || (watched.Count == 0 && charted.Length == 0)) return 0;

        sound.Readings(watched, charted);

        var readings = State.Take(watched.Count + charted.Sum(chart => chart.Trace.Samples.Length));
        watched.CopyTo(readings);

        var at = watched.Count;

        foreach (var chart in charted)
        {
            chart.Trace.Samples.CopyTo(readings[at..]);
            at += chart.Trace.Samples.Length;
        }

        return State.Address;
    }

    /// <summary>A value the editor wrote into its sound's block, a knob or a note, played here as it was written.</summary>
    [JSExport]
    public static void Play(string key, double value) => sound?.Play(key, (float)value);

    /// <summary>Works the sound out at <paramref name="factor"/> times the output rate from here on; a factor not on offer is ignored.</summary>
    [JSExport]
    public static void Oversample(int factor)
    {
        if (sound is not null && AudioRenderer.Oversamples.Contains(factor)) sound.Oversample = factor;
    }

    /// <summary>The width over the height of the picture the sound belongs to, which Coordinates' <c>aspect</c> reads.</summary>
    [JSExport]
    public static void Aspect(double aspect)
    {
        if (sound is not null) sound.Aspect = (float)aspect;
    }

    private static string? Open(Func<Opened> open, int width, int height, string part)
    {
        if (part is not (SoundPart or PicturePart)) return $"There is no part called '{part}': ask for '{SoundPart}' or '{PicturePart}'.";

        sound?.Dispose();
        sound = null;
        picture = null;
        description = null;
        knobs = [];
        hasSound = false;

        try
        {
            var opened = open();

            if (part == SoundPart) sound = new WebSound(opened, width, height);
            else picture = new WebPicture(opened, width, height);

            description = opened.Patch.Description;
            knobs = [.. opened.Patch.Controls ?? []];
            hasSound = opened.Patch.Reaches().Sound;
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
    public static int Hear(int frames) => Render(frames, judged: true);

    /// <summary>
    /// Renders the next <paramref name="frames"/> frames of sound as <see cref="Hear"/> does,
    /// for nobody to hear: a warm-up, which says nothing of how the sound keeps pace.
    /// </summary>
    [JSExport]
    public static int Warm(int frames) => Render(frames, judged: false);

    private static int Render(int frames, bool judged)
    {
        var span = Samples.Take(frames * 2);

        if (sound is null) span.Clear();
        else sound.Hear(span, judged);

        return Samples.Address;
    }

    /// <summary>
    /// Judges the chunks heard since the last look, at <paramref name="seconds"/> on the
    /// worker's clock, and works the sound out a step lower when they keep falling behind;
    /// <c>oversample</c> and <c>behind</c> in <see cref="Status"/> say what came of it.
    /// </summary>
    /// <param name="playing">Whether the sound is running; a stopped sound is not judged.</param>
    [JSExport]
    public static void Judge(double seconds, bool playing) => sound?.Judge(TimeSpan.FromSeconds(seconds), playing);

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

    /// <summary>
    /// The open half as JSON: its size, cost, how far the sound has got, how fast it
    /// renders, and how many chunks heard were timed and came late.
    /// </summary>
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
            status["scriptsMade"] = JsSound.Made;
            status["oversample"] = sound.Oversample;
            status["behind"] = sound.Behind;
            status["timed"] = sound.Timing.Timed;
            status["late"] = sound.Timing.Late;
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

        if (status["open"]!.GetValue<bool>())
        {
            status["description"] = description;
            status["hasSound"] = hasSound;
        }

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
