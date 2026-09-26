namespace Flyback.Plugins.Midi;

/// <summary>
/// Called when a device sends something. Called on whatever thread the backend
/// hears on, which is not the one the window runs on — so it must not block, and
/// whoever handles it is answerable for what it touches.
/// </summary>
public delegate void MidiCallback(MidiMessage message);