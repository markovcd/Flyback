using Flyback.Plugins.Audio;

namespace Flyback.Ui.Audio;

/// <summary>The device that was opened, and what it came from — null when nothing could play.</summary>
internal sealed record AudioSetup(IAudioDevice Device, IAudioOutput? Output = null, string? Failure = null);
