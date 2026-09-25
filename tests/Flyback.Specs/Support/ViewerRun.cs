using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Flyback.Viewer;

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
