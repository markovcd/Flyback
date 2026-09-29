using Avalonia.Controls;
using Flyback.Core.Graph;

namespace Flyback.App.Gallery;

/// <summary>
/// The gallery as a dialog shows it: the box that narrows it, which stays put, and
/// the tiles, which scroll beneath it.
/// </summary>
internal sealed record GalleryParts(
    TextBox Filter,
    Func<Action<IPreset?>, Control> Tiles);