namespace Flyback.Editor.Android;

/// <summary>Android closes an activity, never the editor, so asking does nothing.</summary>
internal sealed class DeviceClose : IClose
{
    public void Close()
    {
    }
}
