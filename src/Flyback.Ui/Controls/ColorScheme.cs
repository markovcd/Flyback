namespace Flyback.Ui.Controls;

/// <summary>Which set of grounds, text and accent the editor is drawn in, as the settings name it.</summary>
/// <remarks>Saved by name, so a scheme is never renamed once shipped.</remarks>
public enum ColorScheme
{
    /// <summary>Blue-gray grounds and the P31 green of the Beam module's trace (ADR-0219).</summary>
    Phosphor,

    /// <summary>Teal-black grounds, cream text and an amber accent.</summary>
    Amber,

    /// <summary>Neutral charcoal, sage text and orange.</summary>
    Graphite,

    /// <summary>Violet-black grounds, warm grays and orange.</summary>
    Midnight,
}
