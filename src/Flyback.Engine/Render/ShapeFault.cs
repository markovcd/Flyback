namespace Flyback.Engine.Render;

/// <summary>
/// Why a file could not be read as a drawing, or <see cref="None"/> where it could.
/// A value rather than an exception, for the reason <see cref="SoundFault"/> is one.
/// </summary>
public enum ShapeFault
{
    None,

    /// <summary>Nothing at that path.</summary>
    Missing,

    /// <summary>Not an .svg, an .obj or a .png.</summary>
    Unsupported,

    /// <summary>Something is there and it is not the format its name says.</summary>
    NotShape,

    /// <summary>On another machine, where a patch is not allowed to reach.</summary>
    Elsewhere,

    /// <summary>Read, and nothing in it draws.</summary>
    Empty,

    /// <summary>Larger than <see cref="ShapeReader.MostBytes"/>, or holding more than <see cref="ShapeReader.MostPoints"/> points or <see cref="ShapeLayout.MostStrokes"/> strokes.</summary>
    TooBig,
}
