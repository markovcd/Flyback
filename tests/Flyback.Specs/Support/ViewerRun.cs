using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.Engine.Graph;
using Flyback.Ui;
using Flyback.Ui.Controls;
using Flyback.Plugins.Hosting;
using Flyback.Viewer.Desktop;
using Flyback.Host;

namespace Flyback.Specs.Support;

/// <summary>
/// flyback-viewer playing the scenario's patch, headless: its own arguments parsed and
/// its own file read, then the window its container builds. Silent, and drawn on the
/// processor, since there is no sound card and no graphics card here.
/// </summary>
public sealed class ViewerRun(PatchContext context, HeadlessTurn turn) : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-viewer-specs");

    private ViewerWindow? window;

    /// <summary>Plays the patch as <c>flyback-viewer patch.fbk</c> with <paramref name="arguments"/> after it.</summary>
    public void Play(params string[] arguments)
    {
        var path = Path.Combine(folder.FullName, "patch.fbk");
        File.WriteAllText(path, PatchIO.ToJson(context.Patch));

        var settings = new OutputSettings();
        var error = new StringWriter();
        ViewerOptions? settled = null;

        ViewerArguments.Build(settings, options => { settled = options; return 0; }, error)
            .Parse([path, "--cpu", "--no-audio", .. arguments])
            .Invoke();

        var options = settled ?? throw new InvalidOperationException($"flyback-viewer refused: {error}");

        var (opened, _) = ViewerSource.Resolve(options, settings, PluginCatalog.Empty, new PresetLibrary(folder.FullName), error)
            ?? throw new InvalidOperationException($"flyback-viewer could not open the patch: {error}");

        Run(() =>
        {
            window = ViewerServices.Window(new ViewerLaunch(opened, null, options));
            window.Show();
            Settle();
        });
    }

    /// <summary>Presses a key over the picture.</summary>
    public void Press(PhysicalKey key) =>
        Run(() =>
        {
            var open = window ?? throw new InvalidOperationException("the viewer is not playing");

            // In the same turn as the key, so no other scenario's window takes the keyboard in between.
            open.Activate();
            open.KeyPressQwerty(key, RawInputModifiers.None);
            open.KeyReleaseQwerty(key, RawInputModifiers.None);
            Settle();
        });

    /// <summary>Taps the picture once, or twice for a double-click.</summary>
    public void TapPicture(int times = 1) =>
        Run(() =>
        {
            var open = window ?? throw new InvalidOperationException("the viewer is not playing");
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
        window!.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.Paused
            : (bool?)null);

    /// <summary>Whether the viewer's window has the whole screen.</summary>
    public bool FullScreen => Run(() => window!.WindowState == WindowState.FullScreen);

    /// <summary>What <c>--report</c> would print now, a line each.</summary>
    public IReadOnlyList<string> Report => Run(() => window!.Player.Report().Lines().ToList());

    /// <summary>What the line in the picture's corner says, or null while it is not showing.</summary>
    public string? Stats => Run(() =>
        window!.GetVisualDescendants().OfType<StatsOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } stats
            ? stats.Said
            : null);

    /// <summary>Which edge of the picture the transport waits at, or null where it has none.</summary>
    public Avalonia.Layout.VerticalAlignment? TransportEdge => Run(() =>
        window!.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.VerticalAlignment
            : (Avalonia.Layout.VerticalAlignment?)null);

    /// <summary>Whether the transport has a sound button, or null where there is no transport showing.</summary>
    public bool? TransportHasSound => Run(() =>
        window!.GetVisualDescendants().OfType<TransportOverlay>().SingleOrDefault() is { IsEffectivelyVisible: true } transport
            ? transport.HasSound
            : (bool?)null);

    public void Dispose()
    {
        try
        {
            if (window is { } open)
            {
                window = null;
                Run(() =>
                {
                    open.Close();
                    Dispatcher.UIThread.RunJobs();
                });
            }
        }
        finally
        {
            turn.Leave(this);
        }

        folder.Delete(recursive: true);
    }

    private void Run(Action act)
    {
        turn.Take(this);
        Headless.Run(act);
    }

    private T Run<T>(Func<T> act)
    {
        turn.Take(this);
        return Headless.Run(act);
    }

    private void Settle()
    {
        window!.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
