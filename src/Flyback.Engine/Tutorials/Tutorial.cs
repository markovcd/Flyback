namespace Flyback.Engine.Tutorials;

/// <summary>A lesson the editor walks a person through, one checked step at a time (ADR-0196).</summary>
/// <param name="Id">The file's name, which is how a tutorial is asked for.</param>
/// <param name="Title">What the column and the list call it.</param>
/// <param name="Summary">What it builds, in a sentence.</param>
/// <param name="From">The preset it opens on.</param>
internal sealed record Tutorial(string Id, string Title, string Summary, string From, IReadOnlyList<TutorialStep> Steps);
