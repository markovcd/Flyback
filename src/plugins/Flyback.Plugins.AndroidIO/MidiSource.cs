using Android.Media.Midi;

namespace Flyback.Plugins.AndroidIO;

/// <summary>One port a device talks from, as Android lists it, and the name a patch knows the device by.</summary>
internal sealed record MidiSource(MidiDeviceInfo Device, int Port, string Name);
