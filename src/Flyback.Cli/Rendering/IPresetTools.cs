using Flyback.Core.Graph;
using Flyback.Cli.Models;

namespace Flyback.Cli.Rendering;

/// <summary>What a preset's media is made with: the renderer here, and ffmpeg.</summary>
internal interface IPresetTools
{
    /// <summary>What <see cref="RenderCommand.Run"/> answers for the patch.</summary>
    int Render(Opened patch, RenderOptions options, TextWriter error, CancellationToken cancellation);

    Task<Ran> Ffmpeg(IReadOnlyList<string> arguments, CancellationToken cancellation);
}