using Avalonia;
using Flyback.Editor;
using Flyback.Ui.Testing;

namespace Flyback.Specs.Support;

/// <summary>The theme the editor runs in, and nothing else of the program's own application.</summary>
public sealed class EditorApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => HeadlessApp.Build<EditorApp>();

    public override void Initialize()
    {
        EditorTheme.Apply(this);
    }
}
