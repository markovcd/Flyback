namespace Flyback.Core.Compile;

/// <summary>
/// Where a Path's drawing comes from, answered by the same library that answers for
/// sounds: a compile asks the <see cref="ISampleLibrary"/> it was handed whether it is
/// one of these too.
/// </summary>
internal interface IShapeLibrary
{
    /// <summary>The drawing a path names, as one closed path, or null where there is none to be had.</summary>
    LoadedShape? FindShape(string path);

    /// <summary>Why the last <see cref="FindShape"/> of this path came back empty, for the complaint.</summary>
    string ExplainShape(string path);
}
