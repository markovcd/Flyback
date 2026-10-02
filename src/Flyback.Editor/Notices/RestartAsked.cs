namespace Flyback.Editor.Notices;

/// <summary>
/// A plugin just installed wants Flyback started again, opening <paramref name="Reopen"/>
/// behind it. The reactor that asks the unsaved question says on it whether the
/// restart went ahead.
/// </summary>
internal sealed record RestartAsked(Reopen? Reopen)
{
    /// <summary>Whether the window is closing to restart, or stayed.</summary>
    public bool Restarted { get; set; }
}
