namespace Flyback.Core.Compile;

/// <summary>
/// Where a patch's pictures come from. The compiler asks; something outside it
/// answers, and owns the reading and the caching.
/// </summary>
/// <remarks>
/// <see cref="ISampleLibrary"/> again and for the same reasons: the compiler must
/// not do file I/O, every edit recompiles the whole patch (ADR-0021), and
/// answering null is what a complaint is made out of. Two interfaces rather than
/// one, because a build that can read a picture and not a sound is a real
/// arrangement.
/// </remarks>
public interface IImageLibrary
{
    /// <summary>The picture a path names, or null where there is none to be had.</summary>
    LoadedImage? Find(string path);

    /// <summary>Why the last <see cref="Find"/> of this path came back empty, for the complaint.</summary>
    string Explain(string path);
}
