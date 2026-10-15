using System.Runtime.InteropServices;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// Whether this process is a copy of Flyback installed from the Microsoft Store: an MSIX
/// package, whose folder is read-only and which the Store updates (ADR-0199).
/// </summary>
internal static class StoreCopy
{
    public static bool Running { get; } = OperatingSystem.IsWindows() && HasPackageIdentity();

    private static bool HasPackageIdentity()
    {
        var length = Marshal.AllocHGlobal(sizeof(int));

        try
        {
            Marshal.WriteInt32(length, 0);

            return GetCurrentPackageFullName(length, 0) != NoPackage;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(length);
        }
    }

    private const int NoPackage = 15700;

    // DllImport rather than LibraryImport, which would need unsafe code in an assembly plugins compile against.
    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(nint length, nint fullName);
}
