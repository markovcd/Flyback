namespace Flyback.Editor.Android;

/// <summary>The activity is typed into only while it is on screen, and the editor runs only then.</summary>
internal sealed class DeviceFocus : IFocus
{
    public bool IsActive => true;
}
