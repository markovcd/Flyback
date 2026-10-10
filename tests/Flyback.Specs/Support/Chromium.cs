using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Tests;
using Reqnroll.UnitTestProvider;

namespace Flyback.Specs.Support;

/// <summary>
/// A headless Chromium with a profile of its own, showing one page that
/// <see cref="BrowserPage"/> drives over the DevTools protocol.
/// </summary>
internal sealed partial class Chromium : IDisposable
{
    /// <summary>How long the browser may take to say where its DevTools listen.</summary>
    private static readonly TimeSpan Starting = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Root in a container runs Chromium only unsandboxed, and the only pages it opens are
    /// the ones <see cref="PageServer"/> serves on loopback. The fake microphone and its
    /// granted permission are what a Line In hears; SwiftShader is WebGL with no graphics card.
    /// </summary>
    private static readonly string[] Flags =
    [
        "--headless",
        "--no-sandbox",
        "--remote-debugging-port=0",
        "--use-fake-device-for-media-stream",
        "--use-fake-ui-for-media-stream",
        "--enable-unsafe-swiftshader",
        "--no-first-run",
        "--window-size=1280,800",
    ];

    /// <summary>
    /// The browser: <c>FLYBACK_CHROMIUM</c>, else the first of the usual names on the path.
    /// The gate's image carries Chrome for Testing's headless shell.
    /// </summary>
    public static string? Path { get; } = Find();

    private readonly Process process;
    private readonly DirectoryInfo profile;

    /// <summary>The page the browser opened with, blank until it is sent somewhere.</summary>
    public BrowserPage Page { get; }

    private Chromium(Process process, DirectoryInfo profile, BrowserPage page)
    {
        this.process = process;
        this.profile = profile;
        Page = page;
    }

    /// <summary>Starts the browser on a blank page, or skips the scenario where there is none.</summary>
    public static Chromium Start(IUnitTestRuntimeProvider runtime)
    {
        Needs.Tool(runtime, TestCategory.Browser, Path is not null, "no Chromium on this machine to open the pages in (set FLYBACK_CHROMIUM)");

        var profile = Directory.CreateTempSubdirectory("flyback-chromium-");
        var start = new ProcessStartInfo(Path!, [.. Flags, $"--user-data-dir={profile.FullName}", "about:blank"])
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        var process = Process.Start(start)!;
        _ = process.StandardOutput.ReadToEndAsync();

        try
        {
            var listening = Listening(process);
            var page = BrowserPage.Connect(BlankPage(listening));
            return new Chromium(process, profile, page);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
            profile.Delete(recursive: true);
            throw;
        }
    }

    /// <summary>The DevTools address the browser prints once it is up, reading the rest of what it says in the background.</summary>
    private static Uri Listening(Process process)
    {
        var said = new List<string>();
        var found = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(() =>
        {
            while (process.StandardError.ReadLine() is { } line)
            {
                if (DevTools().Match(line) is { Success: true } match) found.TrySetResult(new Uri(match.Groups[1].Value));
                else if (!found.Task.IsCompleted) said.Add(line);
            }

            found.TrySetException(new InvalidOperationException($"Chromium ended before it listened: {string.Join('\n', said)}"));
        });

        return found.Task.Wait(Starting)
            ? found.Task.Result
            : throw new TimeoutException($"Chromium did not listen within {Starting.TotalSeconds:0} s.");
    }

    /// <summary>The DevTools address of the page the browser opened with.</summary>
    private static Uri BlankPage(Uri browser)
    {
        using var http = new HttpClient();
        var targets = JsonNode.Parse(http.GetStringAsync(new Uri(browser, "/json/list")).Result)!.AsArray();

        var page = targets.Single(target => (string?)target!["type"] == "page")!;
        return new Uri((string)page["webSocketDebuggerUrl"]!);
    }

    private static string? Find()
    {
        if (Environment.GetEnvironmentVariable("FLYBACK_CHROMIUM") is { Length: > 0 } given) return File.Exists(given) ? given : null;

        string[] names = OperatingSystem.IsWindows()
            ? ["chrome-headless-shell.exe", "chrome.exe"]
            : ["chrome-headless-shell", "chromium", "chromium-browser", "google-chrome"];

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator);

        return names
            .SelectMany(name => directories.Where(d => d.Length > 0).Select(d => System.IO.Path.Combine(d, name)))
            .FirstOrDefault(File.Exists);
    }

    [GeneratedRegex(@"DevTools listening on (ws://\S+)")]
    private static partial Regex DevTools();

    public void Dispose()
    {
        Page.Dispose();

        if (!process.HasExited) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        process.Dispose();

        try
        {
            profile.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A helper process still closing its files; the folder is under the system's temp, for it to clear.
        }
    }
}
