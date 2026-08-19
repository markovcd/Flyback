using System.Runtime.InteropServices;

namespace Flyback.App;

/// <summary>
/// The console this program was started with, if it was started with one.
/// </summary>
/// <remarks>
/// Only Windows has a question here. A console-subsystem program run from a
/// shell inherits that shell's console, which is the whole point of building
/// one — but run from Explorer there is nothing to inherit, so Windows makes a
/// console for it, and a synthesiser with a black window of scrolling nothing
/// beside it is not what anybody double-clicked. Giving that one back is the
/// price of the terminal behaving properly, and it is paid in a console that
/// appears and vanishes rather than one that stays.
/// <para>
/// Everywhere else a process is attached to the terminal that ran it or to
/// nothing at all, nobody has to ask which, and there is nothing to hand back.
/// </para>
/// </remarks>
internal static partial class Terminal
{
    /// <summary>
    /// Whether the console belongs to something else — a shell that is waiting
    /// for this program and will show what it writes.
    /// </summary>
    /// <remarks>
    /// Told by how many processes share the console. Started from a shell there
    /// are at least two, the shell and this; given one by Windows there is
    /// exactly one, because it was made for this process alone. Nothing else
    /// distinguishes the two launches — the process, its arguments and its
    /// environment are identical.
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
    /// Gives back a console this program was given rather than handed. Does
    /// nothing where there was none.
    /// </summary>
    /// <remarks>
    /// The window closes with it, because a console outlives only the processes
    /// attached to it and this was the only one. Failure is ignored for the same
    /// reason it is not checked anywhere else here: there is no console left to
    /// report it on, and a program that would not start because it could not put
    /// a window away is worse than the window.
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
