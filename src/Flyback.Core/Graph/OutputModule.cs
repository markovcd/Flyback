using Flyback.Core.Compile;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Output: the screen and the speakers every patch ends at.</summary>
internal static class OutputModule
{
    /// <summary>
    /// The one sink. Screen and speakers are sockets on a single module rather
    /// than two modules, and every patch has exactly one — see ADR-0037.
    /// </summary>
    internal const string TypeId = "output";

    /// <summary>
    /// Whether a module is the sink, which is what keeps it out of the palette
    /// and out of the delete key — see <see cref="Patch.CanAdd"/> and
    /// <see cref="Patch.Remove"/>.
    /// </summary>
    internal static bool IsSink(string typeId) => typeId == TypeId;

    /// <summary>RGB, so the screen reads three registers.</summary>
    internal const int VideoChannels = 3;

    /// <summary>Stereo, so the speakers read two registers where the screen reads three.</summary>
    internal const int AudioChannels = 2;

    /// <summary>
    /// One of the two programs a patch yields. Both root at the same Output and
    /// differ only in which of its results they read.
    /// </summary>
    /// <param name="Inputs">
    /// Which of the Output's sockets this program walks back from. The others are
    /// never resolved, so nothing upstream of them emits an op — the whole of
    /// ADR-0022's cross-sink dead-code elimination, which with one node has to be
    /// said here.
    /// </param>
    /// <param name="Results">Which of the sink's emit results this program reads.</param>
    internal readonly record struct SinkKind(string Name, Range Inputs, Range Results, int Width);

    /// <summary>The screen's program, walking back from the Output's color.</summary>
    internal static SinkKind Screen => new("screen", ColorPort..LeftPort, 0..1, VideoChannels);

    /// <summary>The speakers' program, walking back from left, right and volume.</summary>
    internal static SinkKind Speakers => new("speakers", LeftPort..(VolumePort + 1), 1..3, AudioChannels);

    // Port indices on the Output, named because three separate places index it
    // and a shifted socket would otherwise be a silent change of meaning.
    /// <summary>The Output's color input, which is the screen.</summary>
    internal const int ColorPort = 0;
    /// <summary>The Output's left input, which is the left speaker.</summary>
    internal const int LeftPort = 1;
    /// <summary>The Output's right input, which is the right speaker.</summary>
    internal const int RightPort = 2;
    /// <summary>The Output's volume input, which scales both speakers.</summary>
    internal const int VolumePort = 3;

    /// <summary>The Output itself, which no patch is without.</summary>
    internal static NodeDef Def() => new(
        TypeId, "Output", ModuleCategories.Output,
        [
            Col("color") with { Help = "The screen." },
            Num("left", 0f, -1f, 1f) with { PatchOnly = true, Help = "The left speaker." },
            Normalled("right", LeftPort, -1f, 1f) with { Help = "The right speaker, carrying 'left' when nothing is patched." },
            Num("volume", 0.5f, 0f, 1f) with
            {
                Help = "Multiplies both speakers. Nought closes them rather than feeding them silence.",
            },
        ],
        [],

        // Three results from one node: the screen reads the first and
        // the speakers the other two. Which of them a given program
        // takes is the only difference between the two compilations.
        (em, i) => [i[0], em.Mul(i[1], i[3]), em.Mul(i[2], i[3])],
        "The patch's one Output: the screen and the speakers. A polyphonic wire into it is "
        + "heard and seen with its voices added.")
    {
        MergesVoices = true,
    };
}
