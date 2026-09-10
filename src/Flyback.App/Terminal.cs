using System.Runtime.InteropServices;

namespace Flyback.App;

/// <summary>
/// The console this program was started with, if it was started with one.
/// </summary>
/// <remarks>
/// Only Windows has a question here. A console-subsystem program run from a shell
/// inherits that shell's console, which is the point of building one; run from
/// Explorer there is nothing to inherit, so Windows makes one, and a synthesiser
/// with a black window of scrolling nothing beside it is not what anybody
/// double-clicked. Everywhere else a process is attached to the terminal that ran
/// it or to nothing, and there is nothing to hand back.
/// </remarks>
internal static partial class Terminal
{
    /// <summary>
    /// Whether the console belongs to something else — a shell that is waiting for
    /// this program and will show what it writes.
    /// </summary>
    /// <remarks>
    /// Told by how many processes share the console: from a shell there are at least
    /// two, and one given by Windows has exactly one. Nothing else distinguishes the
    /// two launches.
    /// </remarks>
    public static bool Inherited
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return true;

            // Two is all this needs to know about: any answer above one means
            // the same thing. A process with no console at all reports zero,
            // which is neither inherited nor ours to release.
            var attached = new uint[2];

            return GetConsoleProcessList(attached, (uint)attached.Length) > 1;
        }
    }

    /// <summary>
    /// Gives back a console this program was given rather than handed. Does nothing
    /// where there was none.
    /// </summary>
    /// <remarks>
    /// The window closes with it, because a console outlives only the processes
    /// attached to it. Failure is ignored: there is no console left to report it on,
    /// and a program that would not start because it could not put a window away is
    /// worse than the window.
    /// </remarks>
    public static void Release()
    {
        if (OperatingSystem.IsWindows()) FreeConsole();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetConsoleProcessList(uint[] processIds, uint count);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
