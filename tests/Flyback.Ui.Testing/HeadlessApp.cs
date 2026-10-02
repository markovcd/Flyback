using Avalonia;
using Avalonia.Headless;

namespace Flyback.Ui.Testing;

/// <summary>The headless Avalonia a test assembly's <c>BuildAvaloniaApp</c> hands back.</summary>
public static class HeadlessApp
{
    /// <summary>Skia under it, and the font the program ships, so a test that looks at what was drawn looks at what a user would see.</summary>
    /// <remarks>
    /// The font is also the only way to find out here whether a glyph the shell asks for
    /// exists: a missing one is a box on a button rather than a failure anywhere.
    /// </remarks>
    public static AppBuilder Build<TApp>() where TApp : Application, new() => AppBuilder
        .Configure<TApp>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
