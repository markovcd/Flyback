namespace Flyback.Editor.Shell;

/// <summary>The kind of place the editor runs in, which settles what it offers (<see cref="EditorHost"/>).</summary>
public enum EditorPlace
{
    /// <summary>A desktop window.</summary>
    Desktop,

    /// <summary>
    /// A phone or tablet (ADR-0184): one screen, every press a finger's, no other program to
    /// start or hand files to, and its plugins linked in.
    /// </summary>
    Device,

    /// <summary>
    /// A browser page (ADR-0162): one thread, nothing opened, saved or recorded, a few settings
    /// in a panel of their own, and the picture drawn at 480 x 270.
    /// </summary>
    Page,
}
