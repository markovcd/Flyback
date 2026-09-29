using Avalonia.Platform;

namespace Flyback.App;

/// <summary>The monitors plugged in now.</summary>
internal interface IMonitors
{
    IReadOnlyList<Screen> All { get; }
}