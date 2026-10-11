namespace Flyback.Editor.Notices;

/// <summary>What a take is may have changed: what pressing Record would do now, and whether one running takes Pause and Open away.</summary>
internal sealed record TakeMarked(TakeMode Mode) : INotice;
