using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins;
using Flyback.Plugins.Effects;
using Flyback.Plugins.Figures;
using Flyback.Plugins.Fractals;
using Flyback.Plugins.Mastering;
using Flyback.Plugins.Picture;
using Flyback.Plugins.Voice;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Draws the preview each shipped module plugin embeds: a handful of its modules
/// side by side, as the canvas paints them.
/// </summary>
/// <remarks>
/// Run by hand, with SHOT_DIR naming somewhere to write to; skipped otherwise.
/// Each PNG becomes the plugin's <c>preview.webp</c> at quality 88.
/// </remarks>
public class PluginPreviewShotTests : UiTest
{
    private static string? Where => Environment.GetEnvironmentVariable("SHOT_DIR");

    /// <summary>
    /// Four fit a row that still reads at the install dialog's width; which four
    /// is whichever say most about the plugin at a glance.
    /// </summary>
    private static readonly (string Name, IFlybackPlugin Plugin, string[] Modules)[] Previews =
    [
        ("picture", new PicturePlugin(), ["star", "text", "palette", "fractal"]),
        ("voice", new VoicePlugin(), ["osc", "drum", "bell", "euclid"]),
        ("effects", new EffectsPlugin(), ["echo", "chorus", "flanger", "phaser"]),
        ("mastering", new MasteringPlugin(), ["eq", "compressor", "limiter", "loudness"]),
        ("figures", new FiguresPlugin(), ["plate", "harmonograph", "overtones"]),
        ("fractals", new FractalsPlugin(), ["mandelbrot", "julia", "orbit"]),
    ];

    private const double Wide = 2200, Tall = 1100;

    /// <summary>Between two modules, and around the row.</summary>
    private const double Gap = 32, Margin = 24;

    [AvaloniaFact]
    public void Draw_the_module_plugins_previews()
    {
        if (Where is not { } folder) return;

        Directory.CreateDirectory(folder);

        var was = NodeCatalog.Current;

        try
        {
            foreach (var (name, plugin, modules) in Previews)
                Shoot(folder, name, was, ModuleCollector.Of(plugin), modules);
        }
        finally
        {
            NodeCatalog.Install(was);
        }
    }

    private void Shoot(string folder, string name, ModuleCatalog was, ModuleCollector plugin, string[] modules)
    {
        var added = was.With(plugin.Provider!, plugin.Modules);

        NodeCatalog.Install(added.Catalog);

        var builder = new PatchBuilder(added.Catalog);
        var x = 0.0;
        var box = default(Rect?);

        foreach (var module in modules)
        {
            var def = plugin.Modules.Single(d => d.TypeId == plugin.Provider!.Id + "." + module);
            var node = builder.Add(def.TypeId, x, 0);
            var bounds = NodeGeometry.Bounds(node, def);

            box = box is { } so ? so.Union(bounds) : bounds;
            x += NodeGeometry.Width + Gap;
        }

        var editor = new NodeEditor { Width = Wide, Height = Tall };
        var window = Show(editor, Wide);

        editor.Patch = builder.Patch;
        Settle(window);
        editor.FrameAll();
        Settle(window);

        var crop = box!.Value.TransformToAABB(editor.GraphToScreen).Inflate(Margin);

        using var frame = window.CaptureRenderedFrame()!;

        if (!new Rect(frame.Size).Contains(crop))
            throw new InvalidOperationException($"{name} is off the {frame.Size} frame: {crop}");

        using var stream = new MemoryStream();
        frame.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;

        using var whole = new Bitmap(stream);
        var size = new PixelSize((int)crop.Width, (int)crop.Height);

        using var target = new RenderTargetBitmap(size);
        using (var context = target.CreateDrawingContext())
            context.DrawImage(whole, crop, new Rect(size.ToSize(1)));

        target.Save(Path.Combine(folder, "plugin-" + name + ".png"), new PngBitmapEncoderOptions());
    }
}
