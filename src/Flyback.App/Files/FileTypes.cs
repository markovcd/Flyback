using Flyback.Plugins.Hosting;
using Flyback.App.Updates;
using Flyback.Core.Graph;
using Flyback.Core.Language;

namespace Flyback.App.Files;

/// <summary>One of the kinds of file Flyback opens, as the operating system is told about it.</summary>
/// <param name="Extension">With its dot.</param>
/// <param name="ProgId">What Windows files it under.</param>
/// <param name="MimeType">What a Linux desktop files it under.</param>
/// <param name="Icon">Its icon's name in <see cref="FileTypes.IconFolder"/>, without an extension.</param>
internal sealed record FileKind(string Extension, string Name, string ProgId, string MimeType, string Icon);

/// <summary>
/// Tells the operating system which of Flyback's programs opens its files, for the
/// user who is signed in and nobody else (ADR-0127).
/// </summary>
public abstract class FileTypes
{
    internal static readonly FileKind[] Kinds =
    [
        new($".{PatchIO.FileExtension}", "Flyback patch", "Flyback.Patch", "application/x-flyback-patch", "patch"),
        new(PatchBundle.Extension, "Flyback bundle", "Flyback.Bundle", "application/x-flyback-bundle", "bundle"),
        new($".{PatchLanguage.FileExtension}", "Flyback text", "Flyback.Text", "application/x-flyback-source", "text"),

        // Whichever program opens it, the editor installs it: the viewer passes it on.
        new(PluginPackage.Extension, "Flyback plugin", "Flyback.Plugin", "application/x-flyback-plugin", "plugin"),
    ];

    /// <summary>Makes <paramref name="opener"/> the program that opens them. Throws if it cannot.</summary>
    public abstract void Apply(FileOpener opener);

    /// <summary>
    /// This machine's way of saying it, for the copy of Flyback that is running, or
    /// null where that copy cannot be told apart from its surroundings.
    /// </summary>
    internal static FileTypes? ForThisCopy()
    {
        if (Installation.Current() is not { } copy) return null;

        if (OperatingSystem.IsMacOS()) return new MacFileTypes();

        var editor = Path.Combine(copy.Root, copy.Executable);
        var viewer = Path.Combine(copy.Root, Path.GetDirectoryName(copy.Executable) ?? "", ViewerName);

        if (OperatingSystem.IsWindows()) return WindowsFileTypes.ForUser(editor, viewer);
        if (OperatingSystem.IsLinux()) return LinuxFileTypes.ForUser(editor, viewer);

        return null;
    }

    /// <summary>The viewer's executable, which is published beside the editor's.</summary>
    internal static string ViewerName => OperatingSystem.IsWindows() ? "flyback-viewer.exe" : "flyback-viewer";

    /// <summary>The folder beside the editor holding an icon per kind, drawn from <c>docs/icons</c>.</summary>
    internal const string IconFolder = "FileIcons";

    /// <summary>The icon <paramref name="kind"/> is shown with, in the format named by <paramref name="extension"/>.</summary>
    private protected static string Icon(string editor, FileKind kind, string extension) =>
        Path.Combine(Path.GetDirectoryName(editor) ?? "", IconFolder, kind.Icon + extension);

    /// <summary>The program a choice names, or null for none.</summary>
    protected static string? Program(FileOpener opener, string editor, string viewer)
    {
        var program = opener switch
        {
            FileOpener.Editor => editor,
            FileOpener.Viewer => viewer,
            _ => null,
        };

        if (program is not null && !File.Exists(program))
            throw new FileNotFoundException($"{Path.GetFileName(program)} is not beside Flyback.", program);

        return program;
    }
}

/// <summary>
/// macOS reads which program opens a file from the bundle's Info.plist, which
/// always names the editor; the editor passes the file on (see <see cref="FlybackApp"/>).
/// </summary>
internal sealed class MacFileTypes : FileTypes
{
    public override void Apply(FileOpener opener)
    {
    }
}
