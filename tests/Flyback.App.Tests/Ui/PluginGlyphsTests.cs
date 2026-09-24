using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins.Effects;
using Flyback.Plugins.Mastering;
using Flyback.Plugins.Picture;
using Flyback.Plugins.Voice;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The marks the shipped plugins draw across their own modules — ADR-0118's one
/// real user. What <see cref="ModuleGlyphsTests"/> promises the engine's own
/// marks, this promises Picture's, Voice's, Effects's and Mastering's.
/// </summary>
public class PluginGlyphsTests : UiTest
{
    /// <summary>
    /// Every module the four shipped plugins register, gathered without touching
    /// disk: a plugin's <c>Register</c> is public and asks nothing but a
    /// registry, so the modules it offers are read straight off it rather than
    /// through <c>PluginHost</c>'s folder scan.
    /// </summary>
    private static IReadOnlyList<NodeDef> ShippedPluginModules()
    {
        var registry = new ModuleCollector();

        new PicturePlugin().Register(registry);
        new VoicePlugin().Register(registry);
        new EffectsPlugin().Register(registry);
        new MasteringPlugin().Register(registry);

        return registry.Modules;
    }

    /// <summary>
    /// The modules a plugin painted its own background for — the ones this file
    /// is about — leaving the rest to be drawn as their category.
    /// </summary>
    private static IEnumerable<(NodeDef Def, ModuleSkin.Palette Skin)> Skinned() =>
        ShippedPluginModules()
            .Select(def => (Def: def, Skin: def.Skin as ModuleSkin.Palette))
            .Where(pair => pair.Skin is not null)!;

    [AvaloniaFact]
    public void A_shipped_plugin_gives_at_least_one_module_a_mark()
    {
        Skinned().Any(pair => pair.Skin.Glyph is not null).ShouldBeTrue(
            "no shipped plugin module has a mark of its own, which is the gap ADR-0118 was cut for");
    }

    /// <summary>
    /// A <see cref="ModuleSkin.Palette"/> that stays in the family matches its
    /// category's own accent exactly — not a color that happens to be close.
    /// </summary>
    [AvaloniaFact]
    public void A_skinned_modules_accent_is_its_categorys_accent()
    {
        foreach (var (def, skin) in Skinned())
        {
            Colors.Of(skin.Accent).ShouldBe(
                Colors.Accent(def.Category), $"'{def.TypeId}' paints an accent its category '{def.Category}' does not own");
        }
    }

    /// <summary>
    /// Avalonia draws nothing for a path it could not read and says nothing
    /// about it (ADR-0116's consequence), so a typo in a plugin's own mark is
    /// invisible until somebody looks. Every glyph has to parse, and to be a
    /// shape of some size inside its own twenty-four units.
    /// </summary>
    [AvaloniaFact]
    public void Every_shipped_plugins_glyph_parses_and_fits_its_box()
    {
        foreach (var (def, skin) in Skinned())
        {
            if (skin.Glyph is not { } glyph) continue;

            var mark = ModuleGlyphs.For(def);

            mark.ShouldNotBeNull($"'{def.TypeId}''s glyph does not parse: {glyph}");

            var bounds = mark!.GetRenderBounds(new Pen(Brushes.White, ModuleGlyphs.Thickness));

            bounds.Width.ShouldBeGreaterThan(ModuleGlyphs.Box / 3, $"'{def.TypeId}' is barely a shape at all");
            bounds.Height.ShouldBeGreaterThan(ModuleGlyphs.Box / 3, $"'{def.TypeId}' is barely a shape at all");

            bounds.X.ShouldBeGreaterThanOrEqualTo(-1, $"'{def.TypeId}' is drawn off the left of its box");
            bounds.Y.ShouldBeGreaterThanOrEqualTo(-1, $"'{def.TypeId}' is drawn off the top of its box");
            bounds.Right.ShouldBeLessThanOrEqualTo(ModuleGlyphs.Box + 1, $"'{def.TypeId}' is drawn off the right");
            bounds.Bottom.ShouldBeLessThanOrEqualTo(ModuleGlyphs.Box + 1, $"'{def.TypeId}' is drawn off the bottom");
        }
    }

    /// <summary>
    /// Two modules drawn with the same path is a copied line rather than a
    /// decision — the same promise <see cref="ModuleGlyphsTests"/> makes for the
    /// engine's own marks.
    /// </summary>
    [AvaloniaFact]
    public void No_two_plugin_modules_share_a_glyph()
    {
        var drawn = Skinned()
            .Where(pair => pair.Skin.Glyph is not null)
            .Select(pair => (pair.Def.TypeId, pair.Skin.Glyph))
            .ToArray();

        foreach (var shared in drawn.GroupBy(d => d.Glyph).Where(g => g.Count() > 1))
            throw new ShouldAssertException(
                $"{string.Join(" and ", shared.Select(s => s.TypeId))} are drawn with the same mark");
    }
}
