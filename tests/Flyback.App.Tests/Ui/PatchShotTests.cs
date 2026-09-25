using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.App.Midi;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.App.Canvas;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Draws the patches the website shows and writes each one out, cropped to the
/// modules.
/// </summary>
/// <remarks>
/// Run by hand, with SHOT_DIR naming somewhere to write to; skipped otherwise,
/// so an ordinary test run neither writes files nor fails for want of a folder.
/// The site shows what the canvas draws rather than a drawing of it, and this is
/// what keeps that true: the pictures are retaken from the code the app paints
/// with, so a module that gains a glyph gains it here too.
/// </remarks>
public class PatchShotTests : UiTest
{
    private static string? Where => Environment.GetEnvironmentVariable("SHOT_DIR");

    [AvaloniaFact]
    public void Draw_the_patches_the_site_shows()
    {
        if (Where is not { } folder) return;

        Directory.CreateDirectory(folder);

        // index.html, beside the same patch written as text: the preset as the
        // gallery hands it over, its arithmetic folded into Expressions.
        Shoot(folder, "plasma", Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn));

        // tutorials.html, one per step of the patch the tutorials build.
        Shoot(folder, "rings", Built(
            """
            rings(freq: 4) |> out.color
            out.volume = 0.5
            """));

        Shoot(folder, "rings-moving", Built(
            """
            rings(freq: 4, offset: t)
              |> autoremap()
              |> color.hsv(value: _, hue: t * 0.1, saturation: 0.85)
              |> out.color
            out.volume = 0.5
            """));

        Shoot(folder, "tone", Built(
            """
            sine(freq: 110) |> out.left
            out.volume = 0.5
            """));

        // tutorials/syntakt.html: the box as the module list adds it, and a
        // sequencer kept to its clock.
        // With an Output beside the columns rather than where the editor would
        // put one for a patch without, so the picture is the box and not the
        // room around it.
        var box = new PatchBuilder(NodeCatalog.BuiltIn);
        PatchClipboard.Paste(box.Patch, InstrumentScaffold.Build(
            "midi:elektron-syntakt",
            InstrumentLibrary.Shipped().Profiles.Single(profile => profile.Name == "Syntakt")));
        box.Add(NodeCatalog.OutputTypeId, 2 * (NodeGeometry.Width + 14), 0, (NodeCatalog.OutputVolumePort, 0.5f));

        Shoot(folder, "syntakt-box", box.Patch);

        Shoot(folder, "syntakt-bass", Built(
            """
            let box   = midi.clock(device: "midi:elektron-syntakt")
            let bass  = notes(in: box.beats, rate: 4) [ C2 ~ C2 G1  C2 ~ D#2 C2 ]
            let pitch = bass |> note(note: _)
            let pluck = bass.gate |> adsr(gate: _, attack: 2ms, decay: 180ms, sustain: 0, release: 60ms)

            saw(freq: pitch) * pluck
              |> filter(cutoff: 900, resonance: 0.4)
              |> out.left

            out.volume = 0.5
            """));
    }

    /// <summary>
    /// The view. Wider and taller than any of these patches needs at the fit's
    /// own 1.4 ceiling, and inside what a window will actually be given — asking
    /// for more height than the screen has ends with the window laid out again
    /// after the frame is captured.
    /// </summary>
    private const double Wide = 2200, Tall = 1100;

    /// <summary>Room around the modules, so a wire leaving one is not cut off.</summary>
    private const double Margin = 24;

    private static Patch Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        if (!load.Ok) throw new InvalidOperationException(load.Report);

        return load.Patch;
    }

    private void Shoot(string folder, string name, Patch patch)
    {
        var editor = NewCanvas(Wide, Tall);
        var window = Show(editor, Wide);

        editor.History.Open(patch);
        Settle(window);

        // Again now the control has a size. The fit that runs when the patch
        // arrives sees whatever bounds the layout had got to, which for a window
        // still being laid out is not the ones it ends up with.
        editor.View.FrameAll();
        Settle(window);

        // Read off the transform rather than hunted for in the pixels, and read
        // before the capture rather than after it: a window still settling its
        // size fits the view again, and a transform read on the far side of that
        // describes a frame that no longer exists.
        var bounds = Around(patch)
            .TransformToAABB(editor.GraphToScreen)
            .Inflate(Margin);

        using var frame = window.CaptureRenderedFrame()!;

        if (!new Rect(frame.Size).Contains(bounds))
            throw new InvalidOperationException($"{name} is off the {frame.Size} frame: {bounds}");

        using var whole = new Bitmap(new MemoryStream(Save(frame)));

        Crop(whole, bounds, Path.Combine(folder, "patch-" + name + ".png"));
    }

    /// <summary>The modules' own bounding box, which is what a reader is here for.</summary>
    private static Rect Around(Patch patch)
    {
        var catalog = NodeCatalog.Current;
        var box = default(Rect?);

        foreach (var node in patch.Nodes)
        {
            if (catalog.Get(node.TypeId) is not { } def) continue;

            var bounds = Geometry.Bounds(node, def);

            box = box is { } so ? so.Union(bounds) : bounds;
        }

        return box ?? throw new InvalidOperationException("The patch has no modules to draw.");
    }

    private static byte[] Save(Bitmap frame)
    {
        using var stream = new MemoryStream();

        frame.Save(stream, new PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    private static void Crop(Bitmap whole, Rect to, string path)
    {
        var size = new PixelSize((int)to.Width, (int)to.Height);

        using var target = new RenderTargetBitmap(size);
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(whole, to, new Rect(size.ToSize(1)));
        }

        target.Save(path, new PngBitmapEncoderOptions());
    }
}
