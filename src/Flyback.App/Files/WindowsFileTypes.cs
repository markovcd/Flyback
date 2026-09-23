using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Flyback.App.Files;

/// <summary>
/// A ProgID per kind under the user's own <c>Software\Classes</c>, so nothing needs
/// an administrator.
/// </summary>
/// <remarks>
/// A default somebody picked in "Open with" is kept by Windows over this, and is
/// theirs to change; Flyback still shows in the list.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsFileTypes(RegistryKey classes, string editor, string viewer, Action changed) : FileTypes
{
    public static WindowsFileTypes ForUser(string editor, string viewer) =>
        new(Registry.CurrentUser.CreateSubKey(@"Software\Classes"), editor, viewer, Announce);

    public override void Apply(FileOpener opener)
    {
        var program = Program(opener, editor, viewer);

        foreach (var kind in Kinds)
        {
            if (program is null) Remove(kind);
            else Write(kind, program);
        }

        changed();
    }

    private void Write(FileKind kind, string program)
    {
        using (var id = classes.CreateSubKey(kind.ProgId))
        {
            id.SetValue("", kind.Name);

            var file = Icon(editor, kind, ".ico");

            using var icon = id.CreateSubKey("DefaultIcon");
            icon.SetValue("", File.Exists(file) ? $"\"{file}\"" : $"\"{program}\",0");

            using var command = id.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{program}\" \"%1\"");
        }

        using var extension = classes.CreateSubKey(kind.Extension);
        extension.SetValue("", kind.ProgId);

        using var others = extension.CreateSubKey("OpenWithProgids");
        others.SetValue(kind.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
    }

    /// <summary>Takes back only what <see cref="Write"/> put there.</summary>
    private void Remove(FileKind kind)
    {
        classes.DeleteSubKeyTree(kind.ProgId, throwOnMissingSubKey: false);

        using (var extension = classes.OpenSubKey(kind.Extension, writable: true))
        {
            if (extension is null) return;

            if (extension.GetValue("") as string == kind.ProgId) extension.DeleteValue("");

            using (var others = extension.OpenSubKey("OpenWithProgids", writable: true))
            {
                others?.DeleteValue(kind.ProgId, throwOnMissingValue: false);
            }

            if (Empty(extension, "OpenWithProgids")) extension.DeleteSubKey("OpenWithProgids");

            if (extension.ValueCount > 0 || extension.SubKeyCount > 0) return;
        }

        classes.DeleteSubKey(kind.Extension, throwOnMissingSubKey: false);
    }

    private static bool Empty(RegistryKey parent, string name)
    {
        using var key = parent.OpenSubKey(name);

        return key is { ValueCount: 0, SubKeyCount: 0 };
    }

    /// <summary>Explorer caches icons and handlers until it is told they moved.</summary>
    private static void Announce() => SHChangeNotify(AssociationsChanged, 0, 0, 0);

    private const int AssociationsChanged = 0x08000000;

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint first, nint second);
}
