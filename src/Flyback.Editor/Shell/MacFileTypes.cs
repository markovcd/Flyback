using Flyback.Editor.Files;



namespace Flyback.Editor.Shell;

/// <summary>
/// macOS reads which program opens a file from the bundle's Info.plist, which
/// always names the editor; the editor passes the file on (see <c>FlybackApp</c>).
/// </summary>
internal sealed class MacFileTypes : FileTypes
{
    public override void Apply(FileOpener opener)
    {
    }
}
