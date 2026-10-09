using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// What a compilation lends an extra that its node cannot tell it: who is
/// asking, where to find a file, and where to put a complaint.
/// </summary>
/// <param name="Title">
/// What the node is called, resolved already — see <see cref="NodeInstance.Title"/>.
/// Passed rather than looked up, so an extra never needs the definition it hangs
/// off.
/// </param>
/// <param name="Samples">
/// Where a path is turned into audio, and null where nothing in this program can
/// open a file — a headless compile has no library.
/// </param>
/// <param name="Report">Where a complaint about this node goes.</param>
/// <param name="Pictures">
/// Where a path is turned into a picture, null the same way
/// <paramref name="Samples"/> is and additionally on every audio program.
/// </param>
public readonly record struct ExtraEnv(
    string Title,
    ISampleLibrary? Samples,
    Action<CompileIssue> Report,
    IImageLibrary? Pictures = null);
