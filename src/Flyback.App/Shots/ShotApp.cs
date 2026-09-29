using Avalonia;
using Avalonia.Headless;

namespace Flyback.App.Shots;

/// <summary>The editor's theme on the headless platform, drawn by Skia as a real window would be.</summary>
internal sealed class ShotApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<ShotApp>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize() => EditorTheme.Apply(this);
}
