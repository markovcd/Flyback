namespace Flyback.Plugins.Midi;

/// <summary>
/// A device that is open and listening. The mirror of
/// <see cref="Audio.IAudioDevice"/>: that is hardware being written to, and this
/// is hardware being read from.
/// </summary>
/// <remarks>
/// Nothing is pulled from this. A port delivers through the callback it was
/// opened with and is otherwise only there to be closed, which is the shape a
/// keyboard actually has — notes happen when a hand moves, and asking between
/// two of them would always get the same answer.
/// </remarks>
public interface IMidiPort : IDisposable
{
    /// <summary>The <see cref="MidiPortInfo.Id"/> this was opened for.</summary>
    string Id { get; }

    /// <summary>Whether it is still listening. False once closed, and once the device is gone.</summary>
    bool IsOpen { get; }
}