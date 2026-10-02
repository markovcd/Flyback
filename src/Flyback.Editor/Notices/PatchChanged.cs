using Flyback.Engine.Graph;

namespace Flyback.Editor.Notices;

/// <summary>The graph on the canvas changed and needs compiling; <paramref name="Opened"/> where it is a patch opened rather than edited.</summary>
internal sealed record PatchChanged(bool Opened);
