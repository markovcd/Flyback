namespace Flyback.Core.Compile;

/// <summary>
/// What a program is being played with: one number per live input it names, read
/// by <see cref="OpCode.LoadLive"/> and written by whoever is holding the keys
/// down.
/// </summary>
/// <remarks>
/// The mirror of <see cref="DelayState"/> — that is what a program remembers
/// between evaluations and this is what is done to it from outside — and alike in
/// the ways that matter: both belong to whoever runs the program, both are swapped
/// alongside it, and a renderer given neither still renders.
/// <para>
/// Keyed by name rather than position, because the two ends never meet: a module
/// asks for <c>keyboard/gate</c> while it is compiled, and something in the shell
/// fills it in when a key moves.
/// </para>
/// <para>
/// <see cref="float"/> rather than the registers' <see cref="double"/>, which is a
/// threading decision: a single float is written and read in one go, so a value
/// here is always a number somebody played. The block as a whole is not atomic —
/// an evaluation may see a new note's pitch beside the old note's gate, which at
/// 192 kHz is five microseconds against a lock on the audio thread.
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
    /// Live input <paramref name="index"/>, and zero for one this program does not
    /// have. Bounds-checked rather than trusted, because the block and the program
    /// are swapped separately: a callback holding the previous program for one more
    /// buffer is reading the new program's block, and must not fault for it.
    /// </summary>
    public double At(int index) => (uint)index < (uint)values.Length ? values[index] : 0d;

    /// <summary>Whether this program reads <paramref name="key"/> at all.</summary>
    public bool Reads(string key) => Array.IndexOf(keys, key) >= 0;

    /// <summary>
    /// Plays <paramref name="key"/>, and does nothing at all where the program does
    /// not read it.
    /// </summary>
    /// <remarks>
    /// A scan rather than a dictionary: a program holds a handful of these, and this
    /// is called when a key moves rather than per sample. Silently ignoring an
    /// unread key is the point — what writes here is playing a keyboard, and should
    /// not have to know which patch is loaded.
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
