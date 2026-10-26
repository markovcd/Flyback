namespace Flyback.Editor.Canvas;

/// <summary>How a value well is drawn: at rest, under the pointer, or turning, finely or not.</summary>
internal enum WellState
{
    Rest,
    Hover,
    Turn,
    Fine,
}
