namespace Flyback.Editor.Notices;

/// <summary>What a take is may have changed: one running takes Pause away, and finishing gives it back.</summary>
internal sealed record TakeMarked : INotice;
