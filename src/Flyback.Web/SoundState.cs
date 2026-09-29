using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Web;

/// <summary>
/// What the picture knows of the sound, carried from the worker that plays it to the
/// page that draws: every Meter's reading and every computer keyboard voice, in the
/// order the picture's program reads them.
/// </summary>
/// <remarks>
/// Both ends compile the same picture program from the same patch, so its live inputs
/// come in the same order on each. A Scope's chart is not carried: the shader reads
/// every table as silence.
/// </remarks>
internal static class SoundState
{
    /// <summary>How many floats the state of <paramref name="picture"/> takes.</summary>
    public static int Length(CompiledPatch picture) => Carried(picture).Length;

    /// <summary>Packs what <paramref name="shown"/>, the picture's block on the sound's side, is played.</summary>
    public static void Write(CompiledPatch picture, LiveValues shown, Span<float> state)
    {
        var at = 0;

        foreach (var index in Carried(picture)) state[at++] = shown.Storage[index];
    }

    /// <summary>Unpacks <paramref name="state"/> into <paramref name="watching"/>.</summary>
    public static void Read(ReadOnlySpan<float> state, CompiledPatch picture, LiveValues watching)
    {
        var at = 0;

        foreach (var index in Carried(picture)) watching.Storage[index] = state[at++];
    }

    /// <summary>The live inputs of <paramref name="picture"/> a Meter or the computer keyboard plays into, by index.</summary>
    private static int[] Carried(CompiledPatch picture) =>
    [
        .. picture.LiveInputs
            .Select((key, index) => (key, index))
            .Where(input => MeterSignals.Is(input.key) || MidiSignal.SourceOf(input.key) == MidiSources.Keyboard)
            .Select(input => input.index),
    ];
}
