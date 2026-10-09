using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>The hardware controller a knob follows.</summary>
/// <param name="Device">The device's stable id, the same string a MIDI In stores.</param>
/// <param name="Channel">1 to 16, or 0 for whichever channel it arrives on.</param>
/// <param name="Controller">The controller number, 0 to 127, or the note's where <see cref="Note"/>.</param>
public sealed record MidiBinding(string Device, int Channel, int Controller)
{
    /// <summary>Whether it is a note struck, a pad or a key, rather than a controller.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Note { get; init; }

    /// <summary>What a panel shows under a knob: <c>CC21</c>, or <c>CC21·2</c> on a channel; <c>Note C3</c> for a note.</summary>
    [JsonIgnore]
    public string Label => Channel == 0 ? What : $"{What}·{Channel}";

    /// <summary>The controller or the note alone: <c>CC21</c>, <c>Note C3</c>.</summary>
    [JsonIgnore]
    public string What => Note ? $"Note {Pitch.Name(Controller)}" : $"CC{Controller}";

    /// <summary>Whether a controller moving on <paramref name="channel"/> is this one.</summary>
    public bool Hears(string device, int channel, int controller) => !Note && Is(device, channel, controller);

    /// <summary>Whether a note struck on <paramref name="channel"/> is this one.</summary>
    public bool Strikes(string device, int channel, int note) => Note && Is(device, channel, note);

    private bool Is(string device, int channel, int number) =>
        Controller == number
        && (Channel == 0 || Channel == channel)
        && string.Equals(Device, device, StringComparison.Ordinal);
}
