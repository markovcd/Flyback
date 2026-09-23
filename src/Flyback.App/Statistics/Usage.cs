using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Flyback.App.Updates;
using Flyback.Core.Graph;

namespace Flyback.App.Statistics;

/// <summary>
/// What a run says about itself: that it started and on what, what it played,
/// whether an assistant was asked, what it did before it ended, and where it fell
/// over if it did (ADR-0094, ADR-0103).
/// </summary>
/// <remarks>
/// The whole of the policy is here — what is counted, what is refused, and how often
/// — so that reading this file is enough to know what leaves the machine. Where it
/// goes is <see cref="Aptabase"/>'s business and nothing here knows.
/// <para>
/// Everything is called from the UI thread, which is where a patch starts playing
/// and where a message is sent, so most of the tallies are unguarded. The two that
/// are not are <see cref="Count"/>, which a MIDI driver's thread reaches, and
/// <see cref="Crashed"/>, which arrives on whichever thread threw.
/// </para>
/// </remarks>
public sealed class Usage
{
    /// <summary>A run that says nothing: statistics switched off, or a build with nowhere to send them.</summary>
    public static Usage Off { get; } = new(null);

    private readonly IUsageSink? sink;
    private readonly Launch launch;
    private readonly Func<TimeSpan> running;

    /// <param name="sink">Where events go, or null to say nothing at all.</param>
    /// <param name="launch">How this run began. Null is an ordinary start.</param>
    /// <param name="running">How long the run has lasted. Null is the real clock.</param>
    internal Usage(IUsageSink? sink, Launch? launch = null, Func<TimeSpan>? running = null)
    {
        this.sink = sink;
        this.launch = launch ?? new Launch();

        if (running is null)
        {
            var clock = Stopwatch.StartNew();
            running = () => clock.Elapsed;
        }

        this.running = running;
    }

    /// <summary>
    /// How many plays a run reports. Playing is the Volume knob crossing nought
    /// (ADR-0079), so without a limit a drag through zero would be a report and an
    /// evening's work would be mostly reports.
    /// </summary>
    public const int MostPlays = 3;

    /// <summary>
    /// The longest a run that is ending is kept for its last events: long enough for
    /// a request that is going to arrive, short enough that nobody waits on one that
    /// is not.
    /// </summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(2);

    /// <summary>The longest name the service keeps a number under.</summary>
    private const int LongestProperty = 40;

    /// <summary>The longest place in the code a crash is said to have been in.</summary>
    private const int LongestPlace = 120;

    /// <summary>What anything Flyback does not ship is called.</summary>
    public const string Other = "other";

    /// <summary>
    /// Everything Flyback ships, by the id it goes by: its own modules' provider,
    /// its plugins, the sound backends in them and the assistants they offer.
    /// </summary>
    /// <remarks>
    /// Anything else — a plugin somebody wrote, a module it adds, a backend it
    /// registers — is sent as <see cref="Other"/>. The name of a plugin a handful of
    /// people have is close enough to a name for the person running it (ADR-0094).
    /// </remarks>
    private static readonly string[] Shipped =
    [
        "flyback",
        "flyback.dpapi",
        "flyback.effects",
        "flyback.gemini",
        "flyback.keychain",
        "flyback.keyring",
        "flyback.mastering",
        "flyback.openai",
        "flyback.picture",
        "flyback.voice",
        "linux.io",
        "mac.io",
        "win.io",
        "alsa",
        "coreaudio",
        "wasapi",
        "gemini",
        "openai",
    ];

    /// <summary>
    /// Where a count, a size or a length is cut into bands: a band is sent rather
    /// than the figure, so that nothing sent is precise enough to pick one machine
    /// or one evening out of the rest (ADR-0103).
    /// </summary>
    private static readonly int[] CoreBands = [1, 2, 4, 8, 16, 32];
    private static readonly int[] MemoryBands = [2, 4, 8, 16, 32, 64];
    private static readonly int[] ScreenBands = [720, 1080, 1440, 2160, 2880];
    private static readonly int[] MinuteBands = [1, 5, 15, 30, 60, 120, 240, 480];
    private static readonly int[] TimesBands = [0, 1, 2, 5, 10];
    private static readonly int[] PlayBands = [0, 1, 2, 5, 10, 25];
    private static readonly int[] FrameRateBands = [10, 20, 30, 45, 55, 90, 140];

    /// <summary>
    /// The assemblies Flyback ships, which are the only ones a crash's place is
    /// read from: a plugin somebody wrote may call its namespace anything at all.
    /// </summary>
    private static readonly HashSet<string?> ShippedAssemblies = new(StringComparer.Ordinal)
    {
        "Flyback",
        "Flyback.Core",
        "Flyback.Engine",
        "Flyback.Plugins",
        "Flyback.Plugins.Dpapi",
        "Flyback.Plugins.Effects",
        "Flyback.Plugins.Gemini",
        "Flyback.Plugins.Keychain",
        "Flyback.Plugins.Keyring",
        "Flyback.Plugins.LinuxIO",
        "Flyback.Plugins.MacIO",
        "Flyback.Plugins.Mastering",
        "Flyback.Plugins.OpenAi",
        "Flyback.Plugins.Picture",
        "Flyback.Plugins.Voice",
        "Flyback.Plugins.WinIO",
    };

    /// <summary>
    /// What a crash may be said to have been: an exception type whose name starts with
    /// one of these is the platform's, one from <see cref="ShippedAssemblies"/> is
    /// ours, and anything else is one a plugin somebody wrote threw, and is
    /// <see cref="Other"/>.
    /// </summary>
    private static readonly string[] PlatformNamespaces = ["System.", "Microsoft.", "Avalonia.", "SkiaSharp."];

    private bool stopped;
    private bool started;
    private bool askedAnyone;
    private bool ended;
    private int crashed;
    private int plays;

    /// <summary>Whether a plugin Flyback does not ship loaded, which makes a preset's name somebody else's.</summary>
    private bool strangers;

    /// <summary>The tally last reported, so the same patch played again is not reported again.</summary>
    private string? lastPlayed;

    /// <summary>How many times each thing in <see cref="Used"/> was done, indexed by it.</summary>
    private readonly int[] done = new int[Enum.GetValues<Used>().Length];

    /// <summary>How many status ticks fell in each frame-rate band, and on which renderer.</summary>
    private readonly Dictionary<string, int> frameRates = new(StringComparer.Ordinal);

    private int gpuTicks;
    private int cpuTicks;

    /// <summary>
    /// A reporter for this run, or <see cref="Off"/> where there is nothing to
    /// report to: statistics switched off, a build that is neither a release nor
    /// made on a developer's machine, or a build carrying no application key, which
    /// is any fork of this repository. A build made on a developer's machine reports
    /// as Aptabase's debug, apart from the releases.
    /// </summary>
    public static Usage Start(UsageSettings settings, Launch? launch = null) =>
        Start(settings, ReleaseFeed.Running(), launch, LocalBuild());

    /// <param name="settings"><inheritdoc cref="Start(UsageSettings, Launch?)"/></param>
    /// <param name="running">
    /// The release this is, or null for a build that is not one. Taken rather than
    /// read from the assembly so a test can say which it is.
    /// </param>
    /// <param name="launch">How this run began.</param>
    /// <param name="local">The version of a build made on a developer's machine, or null for any other.</param>
    internal static Usage Start(UsageSettings settings, Version? running, Launch? launch = null, string? local = null)
    {
        if (!settings.SendUsageStatistics) return Off;

        if (running is null && local is null)
        {
            Trace.WriteLine("usage: not a release build, so nothing is counted");
            return Off;
        }

        var opened = local is not null ? Aptabase.Open(local, debug: true) : Aptabase.Open(running!);

        if (opened is not { } aptabase)
        {
            Trace.WriteLine("usage: this build carries no application key, so nothing is counted");
            return Off;
        }

        return new Usage(aptabase, launch);
    }

    /// <summary>
    /// This build's version where the build marked itself made on a developer's
    /// machine, which one that embeds the local release key does (ReleaseKey.targets).
    /// </summary>
    private static string? LocalBuild()
    {
        var assembly = typeof(Usage).Assembly;

        return assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(a => a is { Key: "LocalBuild", Value: "true" })
            ? assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            : null;
    }

    /// <summary>
    /// Switches it off for the rest of the run, which is what saving the Usage
    /// section with the box cleared does. What has been sent has been sent; nothing
    /// else is.
    /// </summary>
    public void Stop() => stopped = true;

    /// <summary>
    /// That Flyback started, and what it started as: the platform this copy was
    /// published for, the sound backend that opened, the plugins beside it, how it
    /// was launched, and how big the machine and its screens are — in bands.
    /// </summary>
    /// <param name="plugins">Every plugin that loaded, by id. Only the ones Flyback ships are named.</param>
    /// <param name="sound">The backend that opened, or null where none did.</param>
    /// <param name="screens">The height in pixels of each screen, or null where it could not be asked.</param>
    public void Started(IEnumerable<string> plugins, string? sound, IReadOnlyList<int>? screens = null)
    {
        if (Silent || started) return;

        started = true;

        var props = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["platform"] = RuntimeInformation.RuntimeIdentifier,
            ["sound"] = sound is null ? "none" : Known(sound),
            ["first"] = launch.First,
            ["updated"] = launch.Updated,
            ["file"] = launch.File,
            ["cores"] = Band(Environment.ProcessorCount, CoreBands),
            ["memory"] = Band((int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >> 30), MemoryBands),
        };

        if (screens is { Count: > 0 })
        {
            props["screens"] = screens.Count >= 3 ? "3+" : screens.Count.ToString(CultureInfo.InvariantCulture);
            props["screen"] = Band(screens.Max(), ScreenBands);
        }

        foreach (var plugin in plugins)
        {
            var name = Property(plugin);

            if (name == Other) strangers = true;

            props[name] = true;
        }

        Say("started", props);
    }

    /// <summary>
    /// That a patch began to play, and what was in it: one number per module type,
    /// the total, how many wires, and the preset it came from where Flyback ships
    /// that preset. Nothing about the patch itself — not its name, its values or
    /// what it names (ADR-0094).
    /// </summary>
    /// <remarks>
    /// The same tally twice running is said once: playing is a knob crossing nought,
    /// and a patch is usually played several times over while it is being worked on.
    /// </remarks>
    /// <param name="preset">The preset on the canvas, or null for a patch that came from a file or from nothing.</param>
    public void Played(IEnumerable<string> modules, int wires = 0, string? preset = null)
    {
        if (Silent) return;

        Count(Used.Played);

        if (plays >= MostPlays) return;

        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = 0;

        foreach (var module in modules)
        {
            var name = Module(module);
            tally[name] = tally.GetValueOrDefault(name) + 1;
            total++;
        }

        var from = preset is null ? "none" : Preset(preset);
        var said = $"{from}|{wires}|" + string.Join(' ', tally.Select(module => $"{module.Key}:{module.Value}"));

        if (said == lastPlayed) return;

        lastPlayed = said;
        plays++;

        var props = tally.ToDictionary(module => module.Key, module => (object)module.Value, StringComparer.Ordinal);
        props["modules"] = total;
        props["wires"] = wires;
        props["preset"] = from;

        Say("played", props);
    }

    /// <summary>
    /// That an assistant was asked something, and which provider it went to. Named
    /// once a run, counted every time, and never what was asked or what came back.
    /// </summary>
    public void Assistant(string provider)
    {
        if (Silent) return;

        Count(Used.Asked);

        if (askedAnyone) return;

        askedAnyone = true;

        Say("assistant", new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = Known(provider) });
    }

    /// <summary>That something in <see cref="Used"/> was done once more. Said at the end of the run, in a band.</summary>
    /// <remarks>Safe from any thread: a MIDI device is heard on its driver's.</remarks>
    public void Count(Used thing) => Interlocked.Increment(ref done[(int)thing]);

    /// <summary>
    /// What the status bar read on one of its ticks: the frame rate, and whether the
    /// GPU drew it. Kept as a count of ticks a band, and said at the end as the band
    /// the run spent longest in.
    /// </summary>
    public void Drew(double framesPerSecond, bool gpu)
    {
        if (Silent || double.IsNaN(framesPerSecond) || framesPerSecond <= 0) return;

        var band = Band((int)Math.Round(framesPerSecond), FrameRateBands);
        frameRates[band] = frameRates.GetValueOrDefault(band) + 1;

        if (gpu) gpuTicks++;
        else cpuTicks++;
    }

    /// <summary>
    /// That the run is over, and what it did: how long it lasted, how many times a
    /// patch began to play, how often each thing in <see cref="Used"/> was done,
    /// and how fast the picture was drawn and on what — all in bands.
    /// </summary>
    public void Ended()
    {
        if (Silent || ended) return;

        ended = true;

        var props = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["minutes"] = Band((int)running().TotalMinutes, MinuteBands),
            ["plays"] = Band(Times(Used.Played), PlayBands),
        };

        foreach (var thing in Enum.GetValues<Used>())
        {
            if (thing == Used.Played) continue;

            props[Name(thing)] = Band(Times(thing), TimesBands);
        }

        if (gpuTicks + cpuTicks > 0)
        {
            props["renderer"] = gpuTicks >= cpuTicks ? "gpu" : "cpu";
            props["fps"] = frameRates.MaxBy(band => band.Value).Key;
        }

        Say("ended", props);
    }

    /// <summary>
    /// That the run fell over: what kind of exception it was, and the last place in
    /// Flyback's own code it passed through. Never its message, which is where a
    /// path and a name would be, and never its whole trace. Once a run.
    /// </summary>
    public void Crashed(Exception ex)
    {
        if (Silent || Interlocked.Exchange(ref crashed, 1) == 1) return;

        var cause = ex.GetBaseException();

        Say("crashed", new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = KnownType(cause.GetType()),
            ["at"] = Place(cause),
            ["minutes"] = Band((int)running().TotalMinutes, MinuteBands),
        });
    }

    /// <summary>
    /// Waits for what has been said to arrive, for at most <paramref name="most"/>.
    /// For the moment a run ends, which would otherwise take whatever was still on
    /// its way with it.
    /// </summary>
    public void Drain(TimeSpan most) => sink?.Drain(most);

    private bool Silent => sink is null || stopped;

    private int Times(Used thing) => Volatile.Read(ref done[(int)thing]);

    private void Say(string name, IReadOnlyDictionary<string, object> props) =>
        sink?.Send(new UsageEvent(name, props));

    /// <summary>
    /// A figure as the band it falls in: <c>4-7</c>, the top one as <c>32+</c>, and
    /// anything under the first as <c>&lt;2</c>.
    /// </summary>
    internal static string Band(int value, int[] edges)
    {
        if (value < edges[0]) return "<" + edges[0].ToString(CultureInfo.InvariantCulture);

        for (var i = edges.Length - 1; i >= 0; i--)
        {
            if (value < edges[i]) continue;

            var low = edges[i].ToString(CultureInfo.InvariantCulture);

            if (i == edges.Length - 1) return low + "+";

            var high = edges[i + 1] - 1;

            return high == edges[i] ? low : $"{low}-{high.ToString(CultureInfo.InvariantCulture)}";
        }

        return "<" + edges[0].ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A thing in <see cref="Used"/> as it is filed: its name with a small first letter.</summary>
    private static string Name(Used thing)
    {
        var name = thing.ToString();

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// A preset's name as it may be sent. The engine's own are always named; a
    /// plugin's are named only while every plugin that loaded is one Flyback ships,
    /// because a preset a stranger's plugin offers is that stranger's name for it.
    /// </summary>
    private string Preset(string name) =>
        Presets.All.Any(preset => preset.Name == name) || !strangers ? name : Other;

    /// <summary>An exception's type by its full name where it is ours or the platform's, and <see cref="Other"/> where not.</summary>
    internal static string KnownType(Type type) =>
        type.FullName is { } name
        && (FromFlyback(type) || PlatformNamespaces.Any(known => name.StartsWith(known, StringComparison.Ordinal)))
            ? name
            : Other;

    private static bool FromFlyback(Type type) => ShippedAssemblies.Contains(type.Assembly.GetName().Name);

    /// <summary>
    /// The innermost method of Flyback's own that the exception passed through, as
    /// a type and a method — no file, no line, no argument — or <see cref="Other"/>
    /// where it never reached Flyback's code.
    /// </summary>
    /// <remarks>
    /// An async method is compiled into a state machine nested in the type that
    /// wrote it, so a frame there is said as the method it came from rather than as
    /// <c>&lt;OpenAsync&gt;d__12.MoveNext</c>.
    /// </remarks>
    internal static string Place(Exception ex)
    {
        foreach (var frame in new StackTrace(ex, fNeedFileInfo: false).GetFrames())
        {
            if (frame.GetMethod() is not { DeclaringType: { } type } method) continue;

            if (!FromFlyback(type)) continue;

            var name = method.Name;

            while (type.IsNested && type.Name.StartsWith('<') && type.DeclaringType is { } outer)
            {
                var close = type.Name.IndexOf('>');

                if (close > 1) name = type.Name[1..close];

                type = outer;
            }

            var place = $"{type.FullName ?? type.Name}.{name}";

            return place.Length <= LongestPlace ? place : place[^LongestPlace..];
        }

        return Other;
    }

    /// <summary>
    /// An id as it may be sent: itself where Flyback ships whatever it belongs to,
    /// and <see cref="Other"/> where it does not.
    /// </summary>
    internal static string Known(string id) =>
        Shipped.Any(shipped => id == shipped || id.StartsWith(shipped + ".", StringComparison.Ordinal))
            ? id
            : Other;

    /// <summary>
    /// The same, as a name a number can be filed under. A name too long for the
    /// service is filed with the ones that are not ours rather than cut in half,
    /// which would read like a module that exists.
    /// </summary>
    private static string Property(string id)
    {
        var known = Known(id);

        return known.Length <= LongestProperty ? known : Other;
    }

    /// <summary>
    /// A module's type as it may be sent. The engine's own are asked of its
    /// catalog, because nothing in their ids says whose they are: a plugin's
    /// module carries its plugin's id in front and "osc.sine" carries nothing, so
    /// read as <see cref="Known"/> reads a plugin's, every module the engine ships
    /// would be one nobody shipped.
    /// </summary>
    private static string Module(string id) =>
        NodeCatalog.BuiltIn.Get(id) is not null && id.Length <= LongestProperty ? id : Property(id);
}

/// <summary>How a run began — each a yes or a no, and none of them about who began it.</summary>
/// <param name="First">No settings folder existed: the first start on this machine, which is the nearest thing to an install that can be counted without keeping an id.</param>
/// <param name="Updated">A release Flyback downloaded itself installed just before this start.</param>
/// <param name="File">It was started to open a file.</param>
public sealed record Launch(bool First = false, bool Updated = false, bool File = false);

/// <summary>
/// The things a run is counted doing, each said at the end as how many times — in
/// a band — and nothing about what it was done to (ADR-0103).
/// </summary>
public enum Used
{
    /// <summary>A patch began to play.</summary>
    Played,

    /// <summary>A preset was picked from the list.</summary>
    Preset,

    /// <summary>A patch, a bundle or a text file was opened.</summary>
    Opened,

    /// <summary>A patch, a bundle or a text file was saved.</summary>
    Saved,

    /// <summary>A module was added to the canvas.</summary>
    Added,

    /// <summary>A take was recorded to the end and written.</summary>
    Recorded,

    /// <summary>The preview was given the whole window.</summary>
    FullScreen,

    /// <summary>The preview and the canvas swapped places.</summary>
    Swapped,

    /// <summary>The text view was put over the canvas.</summary>
    Text,

    /// <summary>A MIDI device that is not the computer keyboard was heard.</summary>
    Instrument,

    /// <summary>A message was sent to an assistant.</summary>
    Asked,

    /// <summary>The settings window was opened.</summary>
    Settings,
}
