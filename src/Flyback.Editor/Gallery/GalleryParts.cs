using Avalonia.Controls;
using Flyback.Core.Graph;

namespace Flyback.Editor.Gallery;

/// <summary>
/// The gallery as a dialog shows it: the box that narrows it, and the columns that
/// hold it, the cards and the chosen one.
/// </summary>
internal sealed record GalleryParts(
    TextBox Filter,
    Func<Action<IPreset?>, Control> Tiles);
