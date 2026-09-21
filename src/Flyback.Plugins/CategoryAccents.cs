using Flyback.Core.Graph;

namespace Flyback.Plugins;

/// <summary>
/// The engine's own category accents, in the toolkit-free terms a
/// <see cref="ModuleSkin.Palette"/> is built from — for a plugin module that wants
/// to paint its own background while staying in its category's family
/// (ADR-0118). Mirrors <c>Colors.Accent</c> in the shell exactly; internal
/// because it exists to be shared among the plugins in this repository, not as
/// something a third-party plugin is promised.
/// </summary>
internal static class CategoryAccents
{
    public static Swatch Of(string category) => category switch
    {
        ModuleCategories.Output => new Swatch(0xE0, 0x5A, 0x5A),
        ModuleCategories.Sources => new Swatch(0x4A, 0x9E, 0xDE),
        ModuleCategories.Oscillators => new Swatch(0x4F, 0xC3, 0x87),
        ModuleCategories.Timing => new Swatch(0xD8, 0xB0, 0x4A),
        ModuleCategories.Maths => new Swatch(0x7E, 0x86, 0x94),
        ModuleCategories.Geometry => new Swatch(0xB4, 0x84, 0xE0),
        ModuleCategories.Patterns => new Swatch(0xE0, 0xA8, 0x4A),
        ModuleCategories.Color => new Swatch(0xE0, 0x6A, 0xB8),
        ModuleCategories.Feedback => new Swatch(0x3F, 0xC8, 0xC8),
        ModuleCategories.Forms => new Swatch(0x7A, 0x8C, 0xE8),
        ModuleCategories.Pitch => new Swatch(0xA8, 0xCE, 0x52),
        ModuleCategories.Shaping => new Swatch(0xD8, 0x7A, 0x48),
        ModuleCategories.TimeEffects => new Swatch(0x3E, 0xA0, 0xB0),
        ModuleCategories.Measurement => new Swatch(0x92, 0xA8, 0xC8),
        _ => new Swatch(0x88, 0x88, 0x88),
    };
}
