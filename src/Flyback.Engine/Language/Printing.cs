namespace Flyback.Core.Language;

/// <summary>A patch as text, and where in that text each of its modules stands.</summary>
/// <remarks>
/// The map is what makes a printing something to click about: it says which
/// module the words under a caret are. What it holds is positions, so a module
/// folded into the middle of a pipeline is called nothing and is pointed at all
/// the same.
/// </remarks>
/// <param name="Order">
/// The modules whose calls stand in the text, from the first word to the last,
/// so a printing a knob has been written into can be mapped again without
/// printing it afresh — see <see cref="PatchPrinter.Locate"/>.
/// </param>
public sealed record Printing(string Source, SourceMap Map, IReadOnlyList<Guid> Order);