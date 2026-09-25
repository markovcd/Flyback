using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyback.App.Statistics;

/// <summary>
/// Where a run's events go: Aptabase, which counts events for desktop programs and
/// asks for no account from the person running one (ADR-0094).
/// </summary>
/// <remarks>
/// The wire is one POST an event to <c>/api/v0/event</c>, with the application key
/// in a header and the event as JSON. It is written out here rather than taken from
/// a package: it is fifty lines, ADR-0019's caution about dependencies reads the
/// same way here as it does in the engine, and the alternative is a MAUI SDK in an
/// Avalonia program.
/// <para>
/// The session id is the shape the service expects — the second the run began,
/// followed by a random number — and is made at launch and never written down, so
/// nothing joins one run to the next.
/// </para>
/// </remarks>
internal sealed class Aptabase : IUsageSink, IDisposable
{
    /// <summary>What the application key is compiled in as — see the project file.</summary>
    private const string KeyResource = "aptabase-key.txt";

    /// <summary>Where a region's events are taken. A key names its region: <c>A-EU-…</c>.</summary>
    private static readonly Dictionary<string, string> Regions = new(StringComparer.Ordinal)
    {
        ["US"] = "https://us.aptabase.com",
        ["EU"] = "https://eu.aptabase.com",
        ["DEV"] = "https://localhost:3000",
    };

    /// <summary>
    /// A region of <c>SH</c> is somebody running the service themselves, and the
    /// key cannot say where — so the line after it does.
    /// </summary>
    private const string SelfHosted = "SH";

    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient http;
    private readonly string sessionId = SessionId();
    private readonly Body.Info info;

    /// <summary>Events posted and not yet answered, for <see cref="Drain"/> to wait on.</summary>
    private readonly HashSet<Task> inFlight = [];

    private Aptabase(HttpClient http, string version, bool debug)
    {
        this.http = http;
        info = new Body.Info(
            OsName(),
            Environment.OSVersion.Version.ToString(3),
            version,
            $"flyback@{version}",
            debug);
    }

    /// <summary>
    /// A client for the key this build carries, or null where it carries none — a
    /// fork, or this repository before the key was committed, neither of which
    /// counts anything.
    /// </summary>
    /// <param name="transport">The tests' Aptabase. Null is the real one.</param>
    public static Aptabase? Open(Version running, HttpMessageHandler? transport = null) =>
        Open(running.ToString(3), debug: false, transport);

    /// <summary>A client whose events are Aptabase's debug ones where <paramref name="debug"/> says so.</summary>
    /// <param name="transport"><inheritdoc cref="Open(Version, HttpMessageHandler?)"/></param>
    public static Aptabase? Open(string version, bool debug, HttpMessageHandler? transport = null)
    {
        if (EmbeddedKey() is not { } read) return null;

        var (key, host) = read;

        var client = transport is null ? new HttpClient() : new HttpClient(transport, disposeHandler: false);

        client.BaseAddress = host;
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("App-Key", key);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Flyback", version));

        return new Aptabase(client, version, debug);
    }

    /// <summary>The key compiled into this build, or null where there is none.</summary>
    public static (string Key, Uri Host)? EmbeddedKey()
    {
        using var stream = typeof(Aptabase).Assembly.GetManifestResourceStream(KeyResource);

        if (stream is null) return null;

        using var reader = new StreamReader(stream);

        return ReadKey(reader.ReadToEnd());
    }

    /// <summary>
    /// A key and where it is taken, from the file's text, or null for a file with no
    /// key in it — the placeholder a repository without one carries.
    /// </summary>
    /// <remarks>
    /// Blank lines and lines beginning with a hash are ignored, so the file can say
    /// what it is. The first line left is the key, and the second — where there is
    /// one — is the address of a service somebody runs themselves.
    /// </remarks>
    internal static (string Key, Uri Host)? ReadKey(string text)
    {
        var lines = text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        if (lines.Count == 0) return null;

        var key = lines[0];
        var parts = key.Split('-');

        if (parts.Length != 3 || parts[0] != "A") return null;

        if (parts[1] == SelfHosted)
        {
            return lines.Count > 1 && Uri.TryCreate(lines[1], UriKind.Absolute, out var own)
                ? (key, own)
                : null;
        }

        return Regions.TryGetValue(parts[1], out var host) ? (key, new Uri(host)) : null;
    }

    /// <summary>
    /// Starts sending, and returns. Nothing waits for an event and nothing is sent
    /// twice: a statistic that did not arrive is one run out of however many, and
    /// the caller has a window to draw (ADR-0094). Only <see cref="Drain"/> waits.
    /// </summary>
    public void Send(UsageEvent happened)
    {
        var sending = Task.Run(async () =>
        {
            try
            {
                await SendAsync(happened, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"usage: {ex.Message}");
            }
        });

        lock (inFlight) inFlight.Add(sending);

        _ = sending.ContinueWith(
            done => { lock (inFlight) inFlight.Remove(done); },
            TaskScheduler.Default);
    }

    /// <summary>
    /// Waits for whatever is still on its way, and gives up on it after
    /// <paramref name="most"/> — which is how long a closing window is kept open
    /// for a statistic, and no longer (ADR-0103).
    /// </summary>
    public void Drain(TimeSpan most)
    {
        Task[] pending;

        lock (inFlight) pending = [.. inFlight];

        if (pending.Length == 0) return;

        try
        {
            Task.WaitAll(pending, most);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"usage: {ex.Message}");
        }
    }

    /// <summary>Throws where the service refused it or could not be reached.</summary>
    internal async Task SendAsync(UsageEvent happened, CancellationToken cancel)
    {
        var body = new Body(DateTime.UtcNow, sessionId, happened.Name, info, happened.Props);

        using var response = await http.PostAsync(
            "/api/v0/event", JsonContent.Create(body, options: Wire), cancel);

        if (response.IsSuccessStatusCode) return;

        throw new HttpRequestException(
            $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancel)}".Trim());
    }

    public void Dispose() => http.Dispose();

    /// <summary>
    /// The run, in the form the service reads: the second it began, and a random
    /// number after it. Made here and nowhere else — it is not written down, so a
    /// restart is another run and there is nothing to join the two by (ADR-0094).
    /// </summary>
    private static string SessionId()
    {
        var began = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return (began * 100_000_000 + RandomNumberGenerator.GetInt32(0, 100_000_000))
            .ToString(CultureInfo.InvariantCulture);
    }

    private static string OsName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";

        return RuntimeInformation.OSDescription;
    }

    /// <param name="Props">
    /// Written as it stands, which is what keeps this ignorant of what a run says:
    /// a value is a string, a number or a flag, and the far end sorts them.
    /// </param>
    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    private sealed record Body(
        DateTime Timestamp,
        string SessionId,
        string EventName,
        Body.Info SystemProps,
        IReadOnlyDictionary<string, object> Props)
    {
        /// <param name="SdkVersion">
        /// Who is speaking this protocol, which the service asks of every event. It
        /// is Flyback itself rather than any package.
        /// </param>
        /// <param name="IsDebug">A build made on a developer's machine, which Aptabase keeps apart from releases.</param>
        internal sealed record Info(string OsName, string OsVersion, string AppVersion, string SdkVersion, bool IsDebug);
    }
}
