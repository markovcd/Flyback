using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace Flyback.Editor.iOS;

/// <summary>The application delegate, which starts Avalonia on <see cref="DeviceApp"/>.</summary>
[Register("DeviceLaunch")]
public sealed class DeviceLaunch : AvaloniaAppDelegate<DeviceApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(new iOSPlatformOptions { RenderingMode = [iOSRenderingMode.OpenGl, iOSRenderingMode.Metal] });
}
