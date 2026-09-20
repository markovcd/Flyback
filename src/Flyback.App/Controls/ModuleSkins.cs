using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// What the canvas is allowed to draw of a plugin's own background — the Canvas
/// section of the settings window, in the one place everything that paints a
/// module asks.
/// </summary>
/// <remarks>
/// Static, like <see cref="NodeCatalog.Current"/> and for the same reason: it is
/// a property of the running program rather than of any one control, and the
/// three static helpers that read a skin — <see cref="Colors.Palette"/>,
/// <see cref="ModuleGlyphs.For"/> and <see cref="ModuleBackdrop.Of"/> — would
/// otherwise each need it threading through them from a window they know nothing
/// about. The canvas is one control on one thread (ADR-0017).
/// </remarks>
internal static class ModuleSkins
{
    /// <summary>
    /// Whether a module is drawn in the background its author gave it. Off, every
    /// module is its category, which is what a plugin's module was before
    /// ADR-0118 and what somebody who does not want a plugin choosing how their
    /// patch looks is asking for.
    /// </summary>
    public static bool Honored { get; set; } = true;

    /// <summary>Whether an animated picture behind a module runs.</summary>
    public static bool Animated { get; set; } = true;

    /// <summary>
    /// The background this module is drawn with, and null where it has none or
    /// the settings say to ignore the one it has.
    /// </summary>
    public static ModuleSkin? Of(NodeDef def) => Honored ? def.Skin : null;
}
