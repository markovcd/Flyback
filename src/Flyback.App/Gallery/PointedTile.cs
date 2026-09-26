using Avalonia.Controls;
using Flyback.Core.Graph;

namespace Flyback.App.Gallery;

/// <summary>A tile of the gallery: the preset it picks, and the picture it shows it by.</summary>
internal sealed record PointedTile(PatchPreset Preset, Image Picture);