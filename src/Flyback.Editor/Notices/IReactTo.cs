namespace Flyback.Editor.Notices;

/// <summary>
/// A part of the editor that reacts to a <typeparamref name="T"/> notice (ADR-0148).
/// Declared on the class, so its header says what it reacts to and a search for the
/// notice finds every reactor.
/// </summary>
/// <remarks>
/// A reactor never takes the part that raises the notice, and the raiser never takes
/// the reactor, so a reaction can never close a cycle in the container. A reaction
/// that finishes at once returns a completed task; one that waits is awaited before
/// the next reactor runs.
/// </remarks>
internal interface IReactTo<in T> : IReactTo where T : notnull
{
    /// <summary>
    /// Where this runs among the notice's reactors: lowest first. Ties run in the order
    /// they were registered, so a reaction whose place matters says so here.
    /// </summary>
    int Priority => 0;

    Task On(T notice);
}

/// <summary>
/// A part that reacts to some notice, whichever: what lets every reactor be built
/// with the window without knowing the notices.
/// </summary>
/// <remarks>
/// Empty, and <see cref="IReactTo{T}.Priority"/> stays on the notice's own interface:
/// a part reacting to two notices may want a different place in each.
/// </remarks>
internal interface IReactTo;
