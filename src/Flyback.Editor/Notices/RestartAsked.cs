namespace Flyback.Editor.Notices;

/// <summary>
/// Flyback is to start again, opening <paramref name="Reopen"/> behind it, or the
/// document on screen where <paramref name="SameDocument"/> says so. The reactor that
/// asks the unsaved question says on it whether the restart went ahead.
/// </summary>
internal sealed record RestartAsked(Reopen? Reopen, bool SameDocument = false) : INotice
{
    /// <summary>Whether the window is closing to restart, or stayed.</summary>
    public bool Restarted { get; set; }
}
