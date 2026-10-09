namespace Flyback.Engine.Language;

/// <summary>How a number was written, which decides what sockets will take it.</summary>
public enum NumberStyle
{
    Plain,

    /// <summary>Written as a note name, so it belongs on a <see cref="Flyback.Core.Graph.PortDisplay.Note"/> socket.</summary>
    Note,

    /// <summary>Written with a unit of time, so it belongs on a <see cref="Flyback.Core.Graph.PortDisplay.Duration"/> socket.</summary>
    Duration,
}
