using Flyback.Editor.Shell;

namespace Flyback.Editor.iOS;

/// <summary>The app is typed into only while it is on screen, and the editor runs only then.</summary>
internal sealed class DeviceFocus : IFocus
{
    public bool IsActive => true;
}
