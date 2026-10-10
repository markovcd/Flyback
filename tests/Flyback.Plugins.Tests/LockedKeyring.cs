using System.Diagnostics;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Whether the Secret Service's default collection is locked. <c>secret-tool</c> cannot say
/// without raising the unlock prompt, so this reads the collection's <c>Locked</c>
/// property over the session bus, which never prompts.
/// </summary>
internal static class LockedKeyring
{
    private const int PatienceMilliseconds = 5_000;

    /// <summary>True only when the bus says so; no <c>busctl</c>, no bus or no answer is not locked.</summary>
    public static bool IsLocked { get; } = Ask();

    private static bool Ask()
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            var start = new ProcessStartInfo("busctl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in new[]
                     {
                         "--user", "get-property", "org.freedesktop.secrets",
                         "/org/freedesktop/secrets/aliases/default",
                         "org.freedesktop.Secret.Collection", "Locked",
                     })
                start.ArgumentList.Add(argument);

            using var busctl = Process.Start(start);

            if (busctl is null) return false;

            var output = busctl.StandardOutput.ReadToEndAsync();

            if (!busctl.WaitForExit(PatienceMilliseconds))
            {
                busctl.Kill(entireProcessTree: true);
                return false;
            }

            return busctl.ExitCode == 0 && output.Result.Trim() == "b true";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
