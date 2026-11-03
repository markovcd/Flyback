using Avalonia;
using Avalonia.Headless;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Desktop.Shots;

/// <summary>The editor's theme on the headless platform, drawn by Skia as a real window would be.</summary>
internal sealed class ShotApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<ShotApp>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>The color scheme the next session's window is drawn in.</summary>
    public static Scheme Scheme { get; set; } = SchemeLibrary.Default;

    public override void Initialize() => EditorTheme.Apply(this, Scheme);
}
