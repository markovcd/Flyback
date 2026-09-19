using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using Flyback.Core;

namespace Flyback.App.Updates;

/// <summary>
/// Keeping Flyback up to date: a release looked for at startup and downloaded in the
/// background, then installed at the start after that (ADR-0088).
/// </summary>
/// <remarks>
/// A program cannot replace its own files while it runs — on Windows they are locked,
/// and everywhere it would go on running the old ones. So the install is done by the
/// new version, from where it was unpacked: the old one starts it with
/// <see cref="ApplyFlag"/> and exits, and it waits for the old one to be gone, copies
/// itself over the installed copy, and starts that. Nothing extra ships to do this,
/// and every release carries its own installer.
/// <para>
/// The arguments between the two are therefore a contract between versions, and are
/// only ever added to: <c>--apply-update &lt;installed copy&gt; &lt;process id&gt; --
/// &lt;the launch's own arguments&gt;</c>.
/// </para>
/// </remarks>
internal static class Updater
{
    public const string ApplyFlag = "--apply-update";

    /// <summary>How long the new version waits for the old one to exit before giving up.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromMinutes(1);

    public static UpdateFolder Folder => new(Path.Combine(GlobalConstants.DataFolder, "updates"));

    /// <summary>Whether this launch is a new version asked to install itself.</summary>
    public static bool Applying(string[] args) => args.Length > 0 && args[0] == ApplyFlag;

    /// <summary>
    /// Hands this launch to a downloaded version, if one is waiting and may be
    /// installed. True means it has been started and this process should exit
    /// without opening a window.
    /// </summary>
    public static bool HandOff(string[] args, UpdateSettings settings)
    {
        try
        {
            return HandOff(args, settings, Installation.Current(), ReleaseFeed.Running(), Folder);
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the version already installed still works, and
            // opening it is worth more than any update.
            Trace.WriteLine($"updates: could not hand over to the downloaded version: {ex.Message}");
            return false;
        }
    }

    internal static bool HandOff(
        string[] args, UpdateSettings settings, Installation? here, Version? running, UpdateFolder folder)
    {
        if (here is null || running is null) return false;

        if (!settings.CheckForUpdates)
        {
            folder.Clear();
            return false;
        }

        // A note not yet read is an install tried since this launch was asked
        // for — the failed one that started this copy again, typically — and
        // trying once more straight away would turn one start into several.
        if (folder.HasNote || folder.Pending(running) is not { } pending) return false;

        // Both of these pass by themselves in time — a second window closed, a
        // folder's permissions put right — so the download is kept for a start
        // when they have.
        if (!here.Writable() || here.InUseByAnother()) return false;

        var payload = here.PayloadIn(folder.VersionFolder(pending));
        var start = new ProcessStartInfo(Path.Combine(payload, here.Executable))
        {
            UseShellExecute = false,

            // The new version installs with no window of any kind, and on
            // Windows that includes the console a console program is given.
            CreateNoWindow = true,
        };

        start.ArgumentList.Add(ApplyFlag);
        start.ArgumentList.Add(here.Root);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--");

        // A file to open is named relative to where this was started, and the
        // copy that finally opens it will be started from somewhere else.
        foreach (var argument in args)
            start.ArgumentList.Add(argument.StartsWith('-') ? argument : Path.GetFullPath(argument));

        Trace.WriteLine($"updates: installing Flyback {pending.ToString(3)}");

        using var process = Process.Start(start);

        return process is not null;
    }

    /// <summary>
    /// What a launch with <see cref="ApplyFlag"/> does instead of opening a window:
    /// installs the version that is running over the copy it was named, and starts
    /// that copy whether or not it succeeded.
    /// </summary>
    public static void Apply(string[] args)
    {
        var separator = Array.IndexOf(args, "--");

        if (args.Length < 3 || separator < 3
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var waitFor)
            || Installation.Current() is not { } self
            || ReleaseFeed.Running() is not { } version)
            return;

        var target = self with { Root = args[1] };
        var original = args[(separator + 1)..];

        if (!Exited(waitFor)) return;

        Apply(self, target, version, Folder);

        target.Launch(original);
    }

    internal static void Apply(Installation self, Installation target, Version version, UpdateFolder folder)
    {
        var name = $"Flyback {version.ToString(3)}";

        try
        {
            Installer.Install(self.Root, target.Root);

            // The new files are not what the bundle was signed with, and the
            // bundle may hold plugins nobody signed at all.
            if (target.Bundle) Sign(target.Root);

            folder.Note(Installed(version));
        }
        catch (Exception ex)
        {
            var failures = folder.Failed(version);

            folder.Note(failures < UpdateFolder.Attempts
                ? $"Could not update to {name}, and will try again at the next start: {ex.Message}"
                : $"Could not update to {name}: {ex.Message} It can be downloaded from the releases page instead.");
        }
    }

    /// <summary>
    /// The note an install of <paramref name="version"/> leaves when it worked, which
    /// is how the window that opens after knows to show what the release changed.
    /// </summary>
    public static string Installed(Version version) => $"Updated to Flyback {version.ToString(3)}.";

    /// <summary>
    /// Whether the version that started this one has gone. It exits straight after,
    /// so waiting any length of time means it did not.
    /// </summary>
    private static bool Exited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit(ExitWait);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// Looks for a newer release and downloads it, on a thread of its own. Says what
    /// happened on the terminal and nowhere else: nothing about this is a thing to
    /// do anything about until the next start.
    /// </summary>
    public static void CheckInBackground(UpdateSettings settings)
    {
        if (!settings.CheckForUpdates) return;

        if (ReleaseFeed.Running() is not { } running)
        {
            Trace.WriteLine("updates: not a release build, so not looking for one");
            return;
        }

        if (Installation.Current() is not { } here || !here.Writable())
        {
            Trace.WriteLine("updates: this copy cannot be written to, so it is not updated");
            return;
        }

        if (ReleaseSignature.EmbeddedKey() is not { } key)
        {
            Trace.WriteLine("updates: this build carries no release key, so it installs nothing");
            return;
        }

        _ = Task.Run(async () =>
        {
            using (key)
            using (var http = Client(running))
            {
                try
                {
                    var ready = await new UpdateDownloader(http, key, Folder).DownloadAsync(here, running, CancellationToken.None);

                    Trace.WriteLine(ready is null
                        ? "updates: this is the latest release"
                        : $"updates: Flyback {ready.ToString(3)} is ready and installs at the next start");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"updates: {ex.Message}");
                }
            }
        });
    }

    /// <summary>GitHub refuses a request that does not say what is making it.</summary>
    private static HttpClient Client(Version running)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Flyback", running.ToString(3)));

        return http;
    }

    /// <summary>
    /// Signs a Mac bundle for this machine alone, which is all Apple silicon asks of
    /// a program before it will start it. <c>codesign</c> is part of macOS itself.
    /// </summary>
    internal static void Sign(string bundle)
    {
        var start = new ProcessStartInfo("codesign")
        {
            ArgumentList = { "--force", "--deep", "--sign", "-", bundle },
            UseShellExecute = false,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start) ?? throw new IOException("codesign could not be started.");

        var said = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) throw new IOException($"codesign failed: {said.Trim()}");
    }
}
