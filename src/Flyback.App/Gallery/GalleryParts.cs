using Avalonia.Controls;
using Flyback.App.Site;
using Flyback.Core.Graph;

namespace Flyback.App.Gallery;

/// <summary>
/// The gallery as a dialog shows it: the box that narrows it, which stays put, and
/// the tiles, which scroll beneath it.
/// </summary>
internal sealed record GalleryParts(
    TextBox Filter,
    Func<Action<PatchPreset?>, Action<SitePreset>, Control> Tiles);