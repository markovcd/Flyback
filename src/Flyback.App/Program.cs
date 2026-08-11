using Avalonia;

namespace Flyback.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FlybackApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
