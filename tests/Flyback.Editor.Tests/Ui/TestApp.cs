using Avalonia;

namespace Flyback.Editor.Tests.Ui;

/// <summary>Nothing but the theme — the shell's own App does far more than a test wants.</summary>
/// <remarks>
/// The dark variant as well as the theme, because the program asks for it and a
/// test that looked at a light one would be looking at a window nobody has. It
/// is what decides the foreground of a button, so an icon drawn in its parent's
/// color comes out black here and light where it actually runs.
/// </remarks>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        // The editor's own, so the two cannot drift: without it the editor is an
        // unstyled shell and a test would be looking at a control nobody has.
        EditorTheme.Apply(this);
    }
}
