namespace Flyback.Core.Compile;

/// <summary>
/// What a program is being played with: one number per live input it names, read
/// by <see cref="OpCode.LoadLive"/> and written by whoever is holding the keys
/// down.
/// </summary>
/// <remarks>
/// The mirror of <see cref="DelayState"/>. That is what a program remembers
/// between evaluations and this is what is being done to it from outside, and the
/// two are alike in the ways that matter: both belong to whoever is running the
/// program rather than to the program itself, both are swapped alongside it, and
/// a renderer given neither still renders — silence in the one case and nobody
/// playing in the other.
/// <para>
/// Keyed by name rather than by position, because the two ends of this never
/// meet. A module asks for <c>keyboard/gate</c> while it is being compiled;
/// something in the shell fills that in when a key moves, months of code away and
/// on another thread. An index would have to be agreed by counting, and the
/// counting would be done twice.
/// </para>
/// <para>
/// <see cref="float"/> rather than the <see cref="double"/> the registers are, and
/// that is the threading decision as much as a width one: a single float is
/// written and read in one go, so a value here is always a number somebody
/// actually played rather than half of two. What is not atomic is the block as a
/// whole — an evaluation may see a new note's pitch beside the old note's gate,
/// for exactly one evaluation. At 192 kHz that is five microseconds of a wrong
/// answer, and the alternative is a lock on the audio thread.
/// </para>
/// </remarks>
public sealed class LiveValues
{
    private readonly string[] keys;
    private readonly float[] values;

    public LiveValues(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        keys = [.. names];
        values = new float[keys.Length];
    }

    /// <summary>
    /// The block a program with no live inputs gets, and the one a caller that
    /// is not playing anything passes. Shared and empty, so neither has to be
    /// null.
    /// </summary>
    public static LiveValues None { get; } = new([]);

    /// <summary>What the program asks for, in the order <see cref="OpCode.LoadLive"/> numbers them.</summary>
    public IReadOnlyList<string> Keys => keys;

    public int Count => values.Length;

    /// <summary>
    /// Live input <paramref name="index"/>, and zero for one this program does
    /// not have.
    /// </summary>
    /// <remarks>
    /// Bounds-checked rather than trusted, because the block and the program are
    /// swapped separately by anything that has both — a callback holding the
    /// previous program for one more buffer is reading the new program's block,
    /// and it must not fault for it. Zero is what an unplayed input reads
    /// anyway, so the wrong-sized block is briefly a silent one.
    /// </remarks>
    public double At(int index) => (uint)index < (uint)values.Length ? values[index] : 0d;

    /// <summary>Whether this program reads <paramref name="key"/> at all.</summary>
    public bool Reads(string key) => Array.IndexOf(keys, key) >= 0;

    /// <summary>
    /// Plays <paramref name="key"/>, and does nothing at all where the program
    /// does not read it.
    /// </summary>
    /// <remarks>
    /// A scan rather than a dictionary. A program holds a handful of these — five
    /// per instrument, one instrument in nearly every patch that has any — and
    /// this is called when a key moves rather than per sample.
    /// <para>
    /// Silently ignoring an unread key is the point rather than a shortcut. What
    /// writes here is playing a keyboard, and it should not have to know which
    /// patch is loaded or whether the module reading it was deleted a moment ago.
    /// </para>
    /// </remarks>
    public void Set(string key, float value)
    {
        var live = float.IsFinite(value) ? value : 0f;

        for (var i = 0; i < keys.Length; i++)
            if (keys[i] == key)
                values[i] = live;
    }

    /// <summary>Nobody is playing: every input back to nothing.</summary>
    public void Clear() => Array.Clear(values);

    /// <summary>
    /// Lays the block out for a backend that cannot reach in per op — the shader,
    /// which takes these as uniforms before the frame rather than reading them
    /// during it.
    /// </summary>
    public void CopyTo(Span<float> destination)
    {
        var span = values.AsSpan(0, Math.Min(values.Length, destination.Length));

        span.CopyTo(destination);
        destination[span.Length..].Clear();
    }
}
