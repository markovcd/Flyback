namespace Flyback.Editor.Midi;

/// <summary>One instrument plugged in and known by name: its device id, which a binding stores, and its profile.</summary>
internal sealed record PanelInstrument(string Id, InstrumentProfile Profile);
