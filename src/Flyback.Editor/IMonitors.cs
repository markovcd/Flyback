using Avalonia.Platform;

namespace Flyback.Editor;

/// <summary>The monitors plugged in now.</summary>
internal interface IMonitors
{
    IReadOnlyList<Screen> All { get; }
}
