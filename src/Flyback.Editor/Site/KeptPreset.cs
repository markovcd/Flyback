using Flyback.Core.Graph;
using Flyback.Engine.Graph;

namespace Flyback.Editor.Site;

/// <summary>A shared preset kept on this machine, as the site last described it.</summary>
/// <param name="Root">The site it came from.</param>
/// <param name="Opened">When it was last opened from the site.</param>
internal sealed record KeptPreset(Uri Root, SitePreset Preset, DateTimeOffset Opened) : IPreset
{
    public string Name => Preset.Name;

    public string Description => Preset.Description;
}
