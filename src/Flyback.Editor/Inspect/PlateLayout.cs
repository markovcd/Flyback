namespace Flyback.Editor.Inspect;

/// <summary>How the plate is laid out for the hand working it and the room it has.</summary>
internal enum PlateLayout
{
    /// <summary>Under a mouse on a wide panel: the buttons in one row on the band, beside the name, pinned.</summary>
    Wide,

    /// <summary>Under a mouse on a narrow panel: the rows of glyphs under the name.</summary>
    Narrow,

    /// <summary>Under a finger: a strip of worded buttons under the name, and the rest in the menu.</summary>
    Touch,
}
