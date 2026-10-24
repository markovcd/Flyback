using Flyback.Editor.Shell;

namespace Flyback.Editor.Composition;

/// <summary>The system closes an app on a phone or tablet, never the editor, so asking does nothing.</summary>
internal sealed class DeviceClose : IClose
{
    public void Close()
    {
    }
}
