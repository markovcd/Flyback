namespace Flyback.Editor.Notices;

/// <summary>
/// The Files section asks for the preset the window opens on to be picked from the
/// gallery, which says on it which was chosen.
/// </summary>
/// <param name="Current">The preset named now, shown as chosen when the gallery opens.</param>
internal sealed record StartupPatchAsked(string Current) : INotice
{
    /// <summary>The name of the preset picked, or null where the pick was given up.</summary>
    public string? Chosen { get; set; }
}
