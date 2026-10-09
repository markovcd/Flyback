using Avalonia;
using Avalonia.Headless;
using Flyback.Editor;

namespace Flyback.Specs.Support;

/// <summary>The theme the editor runs in, and nothing else of the program's own application.</summary>
public sealed class EditorApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<EditorApp>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        EditorTheme.Apply(this);
    }
}
