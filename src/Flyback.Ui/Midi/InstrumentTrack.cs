namespace Flyback.App.Midi;

/// <param name="Name">What the picker calls it.</param>
/// <param name="Channel">The channel its notes and knobs are on, 1 to 16.</param>
/// <param name="Kind">Which pages it has, matched against <see cref="InstrumentPage.Kind"/>; null for the ordinary kind.</param>
/// <param name="Short">What fits under a knob, "T3"; null to use the name.</param>
internal sealed record InstrumentTrack(string Name, int Channel, string? Kind, string? Short = null);