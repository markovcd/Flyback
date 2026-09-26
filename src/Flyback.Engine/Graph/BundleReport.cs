namespace Flyback.Core.Graph;

/// <summary>
/// What came of packing a patch: the bundle was written whatever happened, and
/// this says what went into it and what did not.
/// </summary>
/// <param name="Carried">
/// The files that went in, as the patch named them before it was rewritten.
/// </param>
/// <param name="Missing">
/// The files that could not be read, again as the patch named them. Not an error:
/// a patch naming a file that has gone still opens and still draws, so a bundle
/// of it does too.
/// </param>
public readonly record struct BundleReport(
    IReadOnlyList<string> Carried,
    IReadOnlyList<string> Missing)
{
    /// <summary>Whether everything the patch names went in, which is what "self-contained" means.</summary>
    public bool Whole => Missing.Count == 0;
}