using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Viewer.Desktop;

/// <summary>A run with no window: its player, and everything the container built for it, let go together.</summary>
internal sealed class PlayerRun(ServiceProvider services) : IDisposable
{
    public ViewerPlayer Player { get; } = services.GetRequiredService<ViewerPlayer>();

    public void Dispose() => services.Dispose();
}