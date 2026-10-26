namespace Flyback.Editor.Canvas;

/// <summary>How a value well is drawn: at rest, under the pointer, turning finely or not, selected, or being typed into.</summary>
internal enum WellState
{
    Rest,
    Hover,
    Turn,
    Fine,
    Focus,
    Edit,
}
