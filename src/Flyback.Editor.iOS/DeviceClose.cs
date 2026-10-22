using Flyback.Editor.Shell;

namespace Flyback.Editor.iOS;

/// <summary>An iOS app never closes itself, so asking does nothing.</summary>
internal sealed class DeviceClose : IClose
{
    public void Close()
    {
    }
}
