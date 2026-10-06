namespace Flyback.Core.Compile;

/// <summary>
/// Where a patch's samples come from. The compiler asks; something outside it answers,
/// and owns the reading and the caching.
/// </summary>
/// <remarks>
/// An interface because the compiler must not do file I/O: every edit recompiles the
/// whole patch (ADR-0021), so a compile that opened a file would open it sixty times a
/// second. Answering null is not an error but what the compiler turns into a complaint
/// naming the module and the file.
/// </remarks>
public interface ISampleLibrary
{
    /// <summary>The audio a path names, or null where there is none to be had.</summary>
    LoadedSample? Find(string path);

    /// <summary>Why the last <see cref="Find"/> of this path came back empty, for the complaint.</summary>
    string Explain(string path);

    /// <summary>The notes a path names, or null where there are none to be had.</summary>
    /// <remarks>A default, so a library that reads sound alone answers that no MIDI file can be opened.</remarks>
    LoadedMidi? FindMidi(string path) => null;

    /// <summary>Why the last <see cref="FindMidi"/> of this path came back empty, for the complaint.</summary>
    string ExplainMidi(string path) => "nothing here can open a MIDI file.";
}
