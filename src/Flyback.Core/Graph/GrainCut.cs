namespace Flyback.Core.Graph;

/// <summary>
/// The texture a <see cref="ModuleSkin.Grain"/> is cut with. A short list on
/// purpose: these are drawn by the shell, so a cut is a thing it knows how to
/// make rather than a thing a plugin describes.
/// </summary>
public enum GrainCut
{
    /// <summary>Diagonal rules, the coarsest of the three.</summary>
    Hatched,

    /// <summary>Fine upright rules, the knurl on a control.</summary>
    Milled,

    /// <summary>A grid of dots, the quietest.</summary>
    Beaded,
}