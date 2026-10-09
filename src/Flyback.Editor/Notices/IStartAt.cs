namespace Flyback.Editor.Notices;

/// <summary>A part with something to do as the editor starts, at one <see cref="StartPhase"/> of it.</summary>
/// <remarks>
/// Run by <see cref="Starting"/>, the parts of a phase in the order they were registered.
/// Unlike a constructor, <see cref="On"/> may raise a notice.
/// </remarks>
internal interface IStartAt
{
    StartPhase Phase { get; }

    Task On();
}
