using Flyback.Engine.Render;

namespace Flyback.Cli.Models;

/// <summary>How long and how large two patches are played to be compared.</summary>
internal sealed record CompareOptions(
    double Seconds = 10d,
    int Width = 320,
    int Height = 180,
    double Fps = MovieRenderer.DefaultFrameRate,
    bool Json = false);
