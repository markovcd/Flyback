using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Midi;

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
/// Two kinds of instrument, and the difference between them is only where the
/// notes come from. The computer's keyboard needs no driver and is always there;
/// hardware arrives through <see cref="IMidiInput"/>, one plugin per platform,
/// and becomes more entries in <see cref="Sources"/> and more voices in the same
/// dictionary. Nothing below this line knows which is which — a program names its
/// instrument by a string and does not care what is behind it.
/// </para>
/// <para>
/// <b>Three threads and one rule.</b> Keys arrive on the UI thread, notes arrive
/// on a thread the MIDI driver owns, and both are read by the thread that plays.
/// Everything this class holds is guarded by <see cref="gate"/>; the reading
/// thread takes no lock at all, because <see cref="LiveValues"/> is single floats
/// and is built for exactly that. The rule that keeps it from deadlocking is that
/// a device is never opened or closed with the lock held — closing one waits for
/// the driver's thread, and that thread may be waiting for this lock.
/// </para>
/// </remarks>
internal sealed class MidiHub(IMidiInput? hardware = null) : IDisposable
{
    /// <summary>
    /// Guards everything below. Held briefly and never across a call into a
    /// device — see the note above about which way that deadlock runs.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>
    /// A fixed set of indexed voices per instrument, made the first time anything
    /// asks. Keyed by the same id a patch stores, so a device that comes back
    /// finds its own voices rather than fresh ones.
    /// </summary>
    private readonly Dictionary<string, List<MidiVoice>> voices = new(StringComparer.Ordinal);

    private const int VoiceCount = 8;

    /// <summary>
    /// The devices currently listening, keyed the same way. Only ever the ones a
    /// running program is actually reading — see <see cref="Listen"/>.
    /// </summary>
    private readonly Dictionary<string, IMidiPort> listening = new(StringComparer.Ordinal);

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
    /// <remarks>
    /// Raised on whichever thread played the note, which for hardware is the
    /// driver's. That is safe because of what is on the other end and only
    /// because of it: both preview surfaces answer <c>Refresh</c> by setting a
    /// flag their own timer reads, and neither touches the visual tree. A handler
    /// added here that did would need marshalling of its own — and would be
    /// paying for it on every note, which is why this does not do it for
    /// everybody in advance.
    /// </remarks>
    public event Action? Played;

    /// <summary>
    /// A device would not open. Said out loud rather than swallowed, because the
    /// patch goes on naming an instrument that is now silent and nothing else
    /// would explain why.
    /// </summary>
    public event Action<string>? Trouble;

    /// <summary>
    /// What there is to play with: the computer's own keys, and then whatever is
    /// plugged in.
    /// </summary>
    /// <remarks>
    /// Asked afresh every time rather than listed once, because devices are
    /// plugged in and pulled out while the program runs. The keyboard is first
    /// and is always there, which is what makes this list never empty and the
    /// picker never a dead end.
    /// </remarks>
    public IReadOnlyList<MidiSource> Sources =>
        [new MidiSource(MidiSources.Keyboard, "Computer keyboard"), .. Ports().Select(p => new MidiSource(p.Id, p.Name))];

    /// <summary>The keys under your hands, mapped to notes.</summary>
    public ComputerKeyboard Keyboard { get; } = new();

    /// <summary>
    /// Points this at the programs that are now running, fills their blocks with
    /// what is already held, and opens or closes devices to match what they read.
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
        lock (gate) following = blocks;

        // Outside the lock, and it has to be: closing a device waits for the
        // driver's thread, which may at that moment be waiting to deliver a note.
        Listen(blocks);

        Publish();
    }

    /// <summary>
    /// A key on the computer's keyboard went down. Does nothing at all where that
    /// key is not a note, which is what lets the window offer this every keystroke
    /// and only take the ones it means.
    /// </summary>
    public bool KeyDown(Avalonia.Input.Key key)
    {
        if (Keyboard.Note(key) is not { } note) return false;

        lock (gate) Down(MidiSources.Keyboard, note, ComputerKeyboard.Velocity);

        Publish();

        return true;
    }

    public bool KeyUp(Avalonia.Input.Key key)
    {
        if (Keyboard.Note(key) is not { } note) return false;

        lock (gate) Up(MidiSources.Keyboard, note);

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

        lock (gate)
        {
            foreach (var voice in Voices(MidiSources.Keyboard)) voice.Silence();
            Keyboard.Octave += moved;
        }

        Publish();

        return $"Keyboard octave: {Keyboard.Range}.";
    }

    /// <summary>
    /// Everything let go. The window calls this when it stops being the window
    /// you are typing into: a key released over another program is a key this
    /// never hears about, and the note would hang for ever.
    /// </summary>
    /// <remarks>
    /// The computer's keys only. A MIDI keyboard is not the window's to lose —
    /// its notes go on arriving while another program has the focus, and letting
    /// them go because somebody alt-tabbed would cut a held chord off for no
    /// reason the person could see.
    /// </remarks>
    public void AllOff()
    {
        bool sounding;

        lock (gate)
        {
            var voices = Voices(MidiSources.Keyboard);
            sounding = voices.Any(voice => voice.Playing);
            foreach (var voice in voices) voice.Silence();
        }

        if (sounding) Publish();
    }

    /// <summary>
    /// Closes every device. The window's, at the end — a port left open outlives
    /// the window that was reading it and holds hardware another program wants.
    /// </summary>
    public void Dispose()
    {
        List<IMidiPort> ports;

        lock (gate)
        {
            ports = [.. listening.Values];
            listening.Clear();
        }

        foreach (var port in ports) Shut(port);
    }

    /// <summary>What is plugged in, and nothing at all where nothing can be asked.</summary>
    /// <remarks>
    /// Total, whatever a backend does. Enumerating hardware is reading something
    /// that may be busy, half-installed or gone since the last call, and none of
    /// that is a reason for a picker not to draw — the same bargain
    /// <see cref="MidiSources.All"/> makes one layer up, kept here as well so the
    /// list this hands out is already safe.
    /// </remarks>
    private IReadOnlyList<MidiPortInfo> Ports()
    {
        if (hardware is null) return [];

        try
        {
            return hardware.Ports;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Opens the devices the running programs read, and closes the ones they do
    /// not.
    /// </summary>
    /// <remarks>
    /// A device is hardware somebody else may want, so it is held only while
    /// something is listening to it. A MIDI In sitting on the canvas wired to
    /// nothing has been eliminated from both programs (ADR-0022) and reads
    /// nothing, so it does not take the keyboard away from whatever else is
    /// using it — the same question <c>MainWindow.Playing</c> asks about the
    /// computer's own keys, asked of the compiled programs for the same reason.
    /// </remarks>
    private void Listen(LiveValues[] blocks)
    {
        if (hardware is null) return;

        var wanted = Ports()
            .Select(port => port.Id)
            .Where(id => blocks.Any(block => Reads(block, id)))
            .ToHashSet(StringComparer.Ordinal);

        List<IMidiPort> shutting;
        List<string> opening;

        lock (gate)
        {
            shutting = [.. listening.Where(entry => !wanted.Contains(entry.Key)).Select(entry => entry.Value)];

            foreach (var port in shutting) listening.Remove(port.Id);

            opening = [.. wanted.Where(id => !listening.ContainsKey(id))];
        }

        // Both outside the lock. Either one waits on a driver, and a driver may
        // at that moment be waiting to hand us a note.
        foreach (var port in shutting) Shut(port);

        foreach (var id in opening) Start(id);
    }

    /// <summary>Whether a program reads any of one instrument's signals.</summary>
    /// <remarks>
    /// All four asked rather than one, because a patch is free to use only the
    /// pitch, and dead-code elimination will have dropped the three it does not
    /// touch. Asking about the gate alone would leave a keyboard unopened for a
    /// patch that only wanted the note.
    /// </remarks>
    private static bool Reads(LiveValues block, string source) =>
        block.Reads(MidiSignal.Key(source, MidiSignal.Pitch))
        || block.Reads(MidiSignal.Key(source, MidiSignal.Gate))
        || block.Reads(MidiSignal.Key(source, MidiSignal.Velocity))
        || block.Reads(MidiSignal.Key(source, MidiSignal.Strikes));

    /// <summary>
    /// Opens one device and starts listening to it. A device that will not open
    /// is said out loud and then let alone — it is not tried again until
    /// something recompiles, because a device another program has taken will
    /// still be taken a millisecond later.
    /// </summary>
    private void Start(string id)
    {
        try
        {
            var port = hardware!.Open(id, message => Receive(id, message));

            lock (gate) listening[id] = port;
        }
        catch (Exception ex)
        {
            Trouble?.Invoke($"Could not open {Named(id)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Closes one device, and lets go of whatever it was holding down.
    /// </summary>
    /// <remarks>
    /// The silence is the point. A device closed mid-chord sends no note-offs —
    /// there is nobody left to send them to — so the notes it was holding would
    /// stay held for the rest of the session, which is the one failure this whole
    /// class exists to avoid.
    /// </remarks>
    private void Shut(IMidiPort port)
    {
        try
        {
            port.Dispose();
        }
        catch
        {
            // A device that will not close cleanly is not something anybody can
            // act on, and it is certainly not worth taking the window down for.
        }

        lock (gate)
        {
            if (!voices.TryGetValue(port.Id, out var sourceVoices)
                || !sourceVoices.Any(voice => voice.Playing)) return;

            foreach (var voice in sourceVoices) voice.Silence();
        }

        Publish();
    }

    /// <summary>
    /// Something arrived from a device. Called on a thread the driver owns, so
    /// what happens here is the write and nothing else — the picture is asked for
    /// on the UI thread by <see cref="Publish"/>.
    /// </summary>
    private void Receive(string source, MidiMessage message)
    {
        lock (gate)
        {
            switch (message.Action)
            {
                case MidiAction.Down:
                    Down(source, message.Note, message.Velocity);
                    break;

                case MidiAction.Up:
                    Up(source, message.Note);
                    break;

                case MidiAction.AllOff:
                    foreach (var voice in Voices(source)) voice.Silence();
                    break;
            }
        }

        Publish();
    }

    /// <summary>What the picker calls an instrument, for saying which one would not open.</summary>
    private string Named(string id) =>
        Ports().FirstOrDefault(port => port.Id == id).Name is { Length: > 0 } name ? name : id;

    /// <summary>
    /// The voice of one instrument, made if this is the first anyone has heard of
    /// it. Call it holding <see cref="gate"/> — it writes the dictionary that
    /// every thread here reads.
    /// </summary>
    private List<MidiVoice> Voices(string source)
    {
        if (voices.TryGetValue(source, out var existing)) return existing;

        return voices[source] = Enumerable.Range(0, VoiceCount).Select(_ => new MidiVoice()).ToList();
    }

    private void Down(string source, int note, float velocity)
    {
        var voices = Voices(source);
        var indexes = ReadIndexes(source);
        var voice = indexes
            .Select(index => voices[index - 1])
            .FirstOrDefault(candidate => candidate.Playing && candidate.Pitch == Math.Clamp(note, 0, 127));

        if (voice is null)
        {
            voice = indexes
                .Select(index => voices[index - 1])
                .FirstOrDefault(candidate => !candidate.Playing);
        }

        if (voice is null)
        {
            // No configured voice is free: cycle back to the first module rather
            // than assigning the note to an index the patch cannot hear.
            voice = voices[indexes[0] - 1];
            voice.Silence();
        }

        voice.Down(note, velocity);
    }

    private IReadOnlyList<int> ReadIndexes(string source)
    {
        var indexes = Enumerable.Range(1, VoiceCount)
            .Where(index => following.Any(block =>
                block.Reads(MidiSignal.Key(source, index, MidiSignal.Pitch))
                || block.Reads(MidiSignal.Key(source, index, MidiSignal.Gate))
                || block.Reads(MidiSignal.Key(source, index, MidiSignal.Velocity))
                || block.Reads(MidiSignal.Key(source, index, MidiSignal.Strikes))))
            .ToList();

        return indexes.Count > 0 ? indexes : Enumerable.Range(1, VoiceCount).ToArray();
    }

    private void Up(string source, int note)
    {
        foreach (var voice in Voices(source))
        {
            if (voice.Playing && voice.Pitch == Math.Clamp(note, 0, 127))
            {
                voice.Up(note);
                return;
            }
        }
    }

    /// <summary>
    /// Writes every voice into every running program's block, and asks for a
    /// frame.
    /// </summary>
    /// <remarks>
    /// The write is under the lock and the asking is not, which is what keeps a
    /// handler free to call back in here without meeting itself.
    /// </remarks>
    private void Publish()
    {
        lock (gate)
        {
            foreach (var block in following)
                foreach (var (source, sourceVoices) in voices)
                    for (var i = 0; i < sourceVoices.Count; i++)
                        sourceVoices[i].WriteTo(block, source, i + 1);
        }

        Played?.Invoke();
    }
}
