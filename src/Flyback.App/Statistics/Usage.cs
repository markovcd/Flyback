using System.Diagnostics;
using System.Runtime.InteropServices;
using Flyback.App.Updates;
using Flyback.Core.Graph;

namespace Flyback.App.Statistics;

/// <summary>
/// What a run says about itself: that it started and on what, what it played, and
/// whether an assistant was asked (ADR-0094).
/// </summary>
/// <remarks>
/// The whole of the policy is here — what is counted, what is refused, and how often
/// — so that reading this file is enough to know what leaves the machine. Where it
/// goes is <see cref="Aptabase"/>'s business and nothing here knows.
/// <para>
/// Everything is called from the UI thread, which is where a patch starts playing
/// and where a message is sent, so none of the tallies are guarded.
/// </para>
/// </remarks>
public sealed class Usage
{
    /// <summary>A run that says nothing: statistics switched off, or a build with nowhere to send them.</summary>
    public static Usage Off { get; } = new(null);

    private readonly IUsageSink? sink;

    /// <param name="sink">Where events go, or null to say nothing at all.</param>
    internal Usage(IUsageSink? sink) => this.sink = sink;

    /// <summary>
    /// How many plays a run reports. Playing is the Volume knob crossing nought
    /// (ADR-0079), so without a limit a drag through zero would be a report and an
    /// evening's work would be mostly reports.
    /// </summary>
    public const int MostPlays = 3;

    /// <summary>The longest name the service keeps a number under.</summary>
    private const int LongestProperty = 40;

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

    private bool stopped;
    private bool started;
    private bool askedAnyone;
    private int plays;

    /// <summary>The tally last reported, so the same patch played again is not reported again.</summary>
    private string? lastPlayed;

    /// <summary>
    /// A reporter for this run, or <see cref="Off"/> where there is nothing to
    /// report to: statistics switched off, a build that is not a release — which is
    /// somebody working on Flyback rather than using it — or a build carrying no
    /// application key, which is any fork of this repository.
    /// </summary>
    public static Usage Start(UsageSettings settings) => Start(settings, ReleaseFeed.Running());

    /// <param name="settings"><inheritdoc cref="Start(UsageSettings)"/></param>
    /// <param name="running">
    /// The release this is, or null for a build that is not one. Taken rather than
    /// read from the assembly because a build with no git checkout beside it — the
    /// Docker one — carries a bare version and cannot be told from a release.
    /// </param>
    internal static Usage Start(UsageSettings settings, Version? running)
    {
        if (!settings.SendUsageStatistics) return Off;

        if (running is null)
        {
            Trace.WriteLine("usage: not a release build, so nothing is counted");
            return Off;
        }

        if (Aptabase.Open(running) is not { } aptabase)
        {
            Trace.WriteLine("usage: this build carries no application key, so nothing is counted");
            return Off;
        }

        return new Usage(aptabase);
    }

    /// <summary>
    /// Switches it off for the rest of the run, which is what saving the Usage
    /// section with the box cleared does. What has been sent has been sent; nothing
    /// else is.
    /// </summary>
    public void Stop() => stopped = true;

    /// <summary>
    /// That Flyback started, and what it started as: the platform this copy was
    /// published for, the sound backend that opened, and the plugins beside it.
    /// </summary>
    /// <param name="plugins">Every plugin that loaded, by id. Only the ones Flyback ships are named.</param>
    /// <param name="sound">The backend that opened, or null where none did.</param>
    public void Started(IEnumerable<string> plugins, string? sound)
    {
        if (Silent || started) return;

        started = true;

        var props = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["platform"] = RuntimeInformation.RuntimeIdentifier,
            ["sound"] = sound is null ? "none" : Known(sound),
        };

        foreach (var plugin in plugins)
            props[Property(plugin)] = true;

        Say("started", props);
    }

    /// <summary>
    /// That a patch began to play, and what was in it: one number per module type,
    /// and the total. Nothing about the patch itself — not its name, its wires, its
    /// values or what it names (ADR-0094).
    /// </summary>
    /// <remarks>
    /// The same tally twice running is said once: playing is a knob crossing nought,
    /// and a patch is usually played several times over while it is being worked on.
    /// </remarks>
    public void Played(IEnumerable<string> modules)
    {
        if (Silent || plays >= MostPlays) return;

        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = 0;

        foreach (var module in modules)
        {
            var name = Module(module);
            tally[name] = tally.GetValueOrDefault(name) + 1;
            total++;
        }

        var said = string.Join(' ', tally.Select(module => $"{module.Key}:{module.Value}"));

        if (said == lastPlayed) return;

        lastPlayed = said;
        plays++;

        var props = tally.ToDictionary(module => module.Key, module => (object)module.Value, StringComparer.Ordinal);
        props["modules"] = total;

        Say("played", props);
    }

    /// <summary>
    /// That an assistant was asked something, and which provider it went to. Once a
    /// run, and never what was asked or what came back.
    /// </summary>
    public void Assistant(string provider)
    {
        if (Silent || askedAnyone) return;

        askedAnyone = true;

        Say("assistant", new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = Known(provider) });
    }

    private bool Silent => sink is null || stopped;

    private void Say(string name, IReadOnlyDictionary<string, object> props) =>
        sink?.Send(new UsageEvent(name, props));

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
    /// catalogue, because nothing in their ids says whose they are: a plugin's
    /// module carries its plugin's id in front and "osc.sine" carries nothing, so
    /// read as <see cref="Known"/> reads a plugin's, every module the engine ships
    /// would be one nobody shipped.
    /// </summary>
    private static string Module(string id) =>
        NodeCatalog.BuiltIn.Get(id) is not null && id.Length <= LongestProperty ? id : Property(id);
}
