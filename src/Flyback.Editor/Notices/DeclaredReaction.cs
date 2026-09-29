namespace Flyback.App.Notices;

/// <summary>One <see cref="IReactTo{T}"/> a registered part declares, so every reactor can be built with the window.</summary>
internal sealed record DeclaredReaction(Type Reaction);
