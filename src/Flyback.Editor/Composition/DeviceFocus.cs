using Flyback.Editor.Shell;

namespace Flyback.Editor.Composition;

/// <summary>An app on a phone or tablet is typed into only while it is on screen, and the editor runs only then.</summary>
internal sealed class DeviceFocus : IFocus
{
    public bool IsActive => true;
}
