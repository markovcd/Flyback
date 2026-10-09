using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Flyback.Plugins.Settings;
using Microsoft.Win32;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// The ASIO drivers installed here, as each driver registers itself under <c>HKLM\SOFTWARE\ASIO</c>,
/// and loading one.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class AsioDrivers
{
    private const string Root = @"SOFTWARE\ASIO";

    /// <summary><c>CLSCTX_INPROC_SERVER</c>: a driver is a DLL loaded into this process.</summary>
    private const uint InProcess = 0x1;

    /// <summary>
    /// Every installed driver, by the key it registered under and the description it gave.
    /// Empty when the registry cannot be read, since a form is not somewhere to fail from.
    /// </summary>
    public static IReadOnlyList<SettingOption> Installed()
    {
        try
        {
            using var asio = Registry.LocalMachine.OpenSubKey(Root);

            if (asio is null) return [];

            var found = new List<SettingOption>();

            foreach (var name in asio.GetSubKeyNames())
            {
                using var key = asio.OpenSubKey(name);

                if (key?.GetValue("CLSID") is not string) continue;

                found.Add(new SettingOption(name, key.GetValue("Description") is string { Length: > 0 } description ? description : name));
            }

            return [.. found.OrderBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Creates the driver registered as <paramref name="name"/>. Called on the thread it will live on.</summary>
    public static AsioDriver Load(string name)
    {
        // A name read from the settings file stays one key under the root.
        using var key = name.Contains('\\', StringComparison.Ordinal) ? null : Registry.LocalMachine.OpenSubKey($@"{Root}\{name}");

        if (key?.GetValue("CLSID") is not string text || !Guid.TryParse(text, out var clsid))
            throw new InvalidOperationException($"the ASIO driver '{name}' is not installed.");

        // A driver answers to its own class id as its interface id.
        var result = CoCreateInstance(clsid, IntPtr.Zero, InProcess, clsid, out var instance);

        if (result != 0 || instance == IntPtr.Zero)
            throw new InvalidOperationException($"could not load the ASIO driver '{name}' (0x{result:X8}).");

        return new AsioDriver(instance);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);
}
