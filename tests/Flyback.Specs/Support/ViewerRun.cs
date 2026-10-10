using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Flyback.Engine.Graph;
using Flyback.Ui;
using Flyback.Ui.Controls;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Viewer.Desktop;
using Flyback.Host;

namespace Flyback.Specs.Support;

/// <summary>
/// flyback-viewer playing the scenario's patch, headless: its own arguments parsed and
/// its own file read, then the window its container builds. Silent, and drawn on the
/// processor, since there is no sound card and no graphics card here.
/// </summary>
public sealed class ViewerRun(PatchContext context, HeadlessTurn turn) : HeadlessWindow(turn)
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-viewer-specs");

    private ViewerWindow Playing => Shown as ViewerWindow ?? throw new InvalidOperationException("the viewer is not playing");

    /// <summary>The sound input plugged in for the run, or null where none was.</summary>
    public FakeSoundInput? Input { get; private set; }

    /// <summary>
    /// Plays the patch with a sound input plugged in, for a Line In to listen through,
    /// on a device that runs without a sound card: a Line In is heard only while the sound plays.
    /// </summary>
    public void PlayWithAnInput(params string[] arguments)
    {
        Input = new FakeSoundInput();
        Play(new SilentAudioDevice(), arguments);
    }

    /// <summary>Plays the patch as <c>flyback-viewer patch.fbk</c> with <paramref name="arguments"/> after it, with no sound device.</summary>
    public void Play(params string[] arguments) => Play(null, arguments);

    /// <summary>Plays the patch on a device that runs without a sound card, from a backend called <paramref name="backend"/>.</summary>
    public void PlayThrough(string backend, params string[] arguments) =>
        Play(new SilentAudioDevice(), arguments, new NamedSoundOutput(backend));

    private void Play(IAudioDevice? device, string[] arguments, IAudioOutput? output = null)
    {
        var path = Path.Combine(folder.FullName, "patch.fbk");
        File.WriteAllText(path, PatchIO.ToJson(context.Patch));

        var settings = new OutputSettings();
        var error = new StringWriter();
        ViewerOptions? settled = null;

        ViewerArguments.Build(settings, options => { settled = options; return 0; }, error)
            .Parse([path, "--cpu", .. device is null ? ["--no-audio"] : Array.Empty<string>(), .. arguments])
            .Invoke();

        var options = settled ?? throw new InvalidOperationException($"flyback-viewer refused: {error}");

        var (opened, _) = ViewerSource.Resolve(options, settings, PluginCatalog.Empty, new PresetLibrary(folder.FullName), error)
            ?? throw new InvalidOperationException($"flyback-viewer could not open the patch: {error}");

        Run(() =>
        {
            var window = ViewerServices.Window(new ViewerLaunch(opened, device, options) { Input = Input, Output = output });
            Shown = window;
            window.Show();
            Settle();
        });
    }

    /// <summary>Presses a key over the picture.</summary>
    public void Press(PhysicalKey key) =>
        Run(() =>
        {
            PressKey(Playing, key);
            Settle();
        });

    /// <summary>Taps the picture once, or twice for a double-click.</summary>
    public void TapPicture(int times = 1) =>
        Run(() =>
        {
            var open = Playing;
            var preview = open.GetVisualDescendants().OfType<PreviewHost>().Single();
            var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), open)!.Value;

            for (var tap = 0; tap < times; tap++)
            {
                open.MouseDown(at, MouseButton.Left);
                open.MouseUp(at, MouseButton.Left);
            }

            Settle();
        });

    /// <summary>Whether the viewer is paused, or null where it has no transport showing.</summary>
    public bool? Paused => Run(() =>
        Playing.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.Paused
            : (bool?)null);

    /// <summary>Whether the viewer's window has the whole screen.</summary>
    public bool FullScreen => Run(() => Playing.WindowState == WindowState.FullScreen);

    /// <summary>What <c>--report</c> would print now, a line each.</summary>
    public IReadOnlyList<string> Report => Run(() => Playing.Player.Report().Lines().ToList());

    /// <summary>What the line in the picture's corner says, or null while it is not showing.</summary>
    public string? Stats => Run(() =>
        Playing.GetVisualDescendants().OfType<StatsOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } stats
            ? stats.Said
            : null);

    /// <summary>Which edge of the picture the transport waits at, or null where it has none.</summary>
    public Avalonia.Layout.VerticalAlignment? TransportEdge => Run(() =>
        Playing.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.VerticalAlignment
            : (Avalonia.Layout.VerticalAlignment?)null);

    /// <summary>Whether the transport has a sound button, or null where there is no transport showing.</summary>
    public bool? TransportHasSound => Run(() =>
        Playing.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.HasSound
            : (bool?)null);

    public override void Dispose()
    {
        Input?.Dispose();
        base.Dispose();
        folder.Delete(recursive: true);
    }
}
