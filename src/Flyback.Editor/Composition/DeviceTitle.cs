using Flyback.Editor.Shell;

namespace Flyback.Editor.Composition;

/// <summary>An app on a phone or tablet shows no title, so the editor's goes nowhere.</summary>
internal sealed class DeviceTitle : ITitle
{
    public void Set(string title)
    {
    }
}
