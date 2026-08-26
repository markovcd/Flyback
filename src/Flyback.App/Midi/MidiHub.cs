using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.App.Midi;

/// <summary>
/// Everything that can play the patch, and the join between what is being played
/// and the programs that are running.
/// </summary>
/// <remarks>
/// The mirror of <c>AudioEngine</c>: that takes what the patch produces to a
/// device, and this takes what a device produces to the patch. Both are the shell
/// rather than the engine, for the reason ADR-0025 gives — the engine has no
/// platform in it and this is where the platform is.
/// <para>
/// Values are pushed rather than pulled. A renderer could ask what is held before
/// each buffer and each frame, but there is nothing to ask *for*: a key moves a
/// few times a second at most, and between two presses every question has the
/// same answer. So a press writes into the blocks of whatever programs are
/// running, and both renderers then read plain floats with nobody to call.
/// </para>
/// <para>
/// One backend so far, and it is the one that needs no driver: the keyboard the
/// computer already has. Hardware arrives as more entries in
/// <see cref="Sources"/> and more voices in the same dictionary — nothing else
/// here changes shape, because a program names its instrument by a string and
/// does not care what is behind it.
/// </para>
/// </remarks>
internal sealed class MidiHub
{
    /// <summary>
    /// One voice per instrument, made the first time anything asks. Keyed by the
    /// same id a patch stores, so a device that comes back finds its own voice
    /// rather than a fresh one.
    /// </summary>
    private readonly Dictionary<string, MidiVoice> voices = new(StringComparer.Ordinal);

    /// <summary>
    /// The blocks of the programs currently running — the picture's and the
    /// sound's. Replaced whole on every recompile, because a program that has
    /// been swapped out is not being read any more and a block nobody reads is
    /// just an array.
    /// </summary>
    private LiveValues[] following = [];

    /// <summary>
    /// Something moved. The picture is redrawn on a timer that skips a frame
    /// when nothing has changed, and a key going down while the clock is stopped
    /// is exactly that: a change with no time behind it.
    /// </summary>
    public event Action? Played;

    /// <summary>
    /// What there is to play with. Only the computer's own keys so far —
    /// hardware is a plugin that has not been written, and until it is this is
    /// the honest list rather than an empty one.
    /// </summary>
    public IReadOnlyList<MidiSource> Sources { get; } =
        [new MidiSource(MidiSources.Keyboard, "Computer keyboard")];

    /// <summary>The keys under your hands, mapped to notes.</summary>
    public ComputerKeyboard Keyboard { get; } = new();

    /// <summary>
    /// Points this at the programs that are now running, and fills their blocks
    /// with what is already held.
    /// </summary>
    /// <remarks>
    /// Filling immediately is the whole reason this is not just an assignment. An
    /// edit recompiles the patch, and a note held across one must still be held
    /// after it — otherwise every knob turned while playing would cut the note
    /// off. The blocks are new arrays each time and start at nought, so what is
    /// held has to be written into them at once rather than waiting for the next
    /// press.
    /// </remarks>
    public void Follow(params LiveValues[] blocks)
    {
        following = blocks;
        Publish();
    }

    /// <summary>The voice of one instrument, made if this is the first anyone has heard of it.</summary>
    public MidiVoice Voice(string source)
    {
        if (voices.TryGetValue(source, out var existing)) return existing;

        return voices[source] = new MidiVoice();
    }

    /// <summary>
    /// A key on the computer's keyboard went down. Does nothing at all where that
    /// key is not a note, which is what lets the window offer this every keystroke
    /// and only take the ones it means.
    /// </summary>
    public bool KeyDown(Avalonia.Input.Key key)
    {
        if (Keyboard.Note(key) is not { } note) return false;

        Voice(MidiSources.Keyboard).Down(note, ComputerKeyboard.Velocity);
        Publish();

        return true;
    }

    public bool KeyUp(Avalonia.Input.Key key)
    {
        if (Keyboard.Note(key) is not { } note) return false;

        Voice(MidiSources.Keyboard).Up(note);
        Publish();

        return true;
    }

    /// <summary>
    /// Moves the computer keyboard's two rows up or down an octave, and says
    /// where they ended up — null when the key was not one of the two that do it.
    /// </summary>
    /// <remarks>
    /// Everything already down is let go first. Two rows of keys are not a
    /// keyboard, and a note released after the shift would be a different note
    /// from the one pressed — so it would never be found and would hang.
    /// </remarks>
    public string? Shift(Avalonia.Input.Key key)
    {
        var moved = key switch
        {
            Avalonia.Input.Key.PageUp => 1,
            Avalonia.Input.Key.PageDown => -1,
            _ => 0,
        };

        if (moved == 0) return null;

        Voice(MidiSources.Keyboard).Silence();
        Keyboard.Octave += moved;
        Publish();

        return $"Keyboard octave: {Keyboard.Range}.";
    }

    /// <summary>
    /// Everything let go. The window calls this when it stops being the window
    /// you are typing into: a key released over another program is a key this
    /// never hears about, and the note would hang for ever.
    /// </summary>
    public void AllOff()
    {
        var sounding = false;

        foreach (var voice in voices.Values)
        {
            sounding |= voice.Playing;
            voice.Silence();
        }

        if (sounding) Publish();
    }

    /// <summary>Writes every voice into every running program's block.</summary>
    private void Publish()
    {
        foreach (var block in following)
            foreach (var (source, voice) in voices)
                voice.WriteTo(block, source);

        Played?.Invoke();
    }
}
