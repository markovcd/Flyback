namespace Flyback.Core.Graph;

/// <summary>The hardware controller a knob follows.</summary>
/// <param name="Device">The device's stable id, the same string a MIDI In stores.</param>
/// <param name="Channel">1 to 16, or 0 for whichever channel it arrives on.</param>
/// <param name="Controller">The controller number, 0 to 127.</param>
public sealed record MidiBinding(string Device, int Channel, int Controller)
{
    /// <summary>What a panel shows under a knob: <c>CC21</c>, or <c>CC21·2</c> on a channel.</summary>
    public string Label => Channel == 0
        ? $"CC{Controller}"
        : $"CC{Controller}·{Channel}";

    /// <summary>Whether a controller moving on <paramref name="channel"/> is this one.</summary>
    public bool Hears(string device, int channel, int controller) =>
        Controller == controller
        && (Channel == 0 || Channel == channel)
        && string.Equals(Device, device, StringComparison.Ordinal);
}