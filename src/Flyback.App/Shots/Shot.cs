using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Settings;
using Flyback.App.Windows;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.App.Shots;

/// <summary>
/// <c>Flyback --shot</c>: the editor's window with a patch open, drawn with no screen and
/// written to a PNG, its picture at a chosen second.
/// </summary>
/// <remarks>
/// The window is the one the program opens, built by the same container, with nothing read
/// from or kept on this machine. The picture is drawn on the processor, and runs the second
/// and a half before the moment first, so a patch that reads its previous frame has one.
/// </remarks>
internal static class Shot
{
    private const int Ok = 0, Failed = 2;

    /// <summary>How long the picture runs up to the moment, and how far apart its frames are.</summary>
    private const double RunUp = 1.5, Step = 1d / 20;

    /// <summary>How long any one wait may take before the shot gives up.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

    public static int Run(string[] args)
    {
        var error = Console.Error;

        if (ShotRequest.Parse(args, error) is not { } request) return Failed;

        var plugins = PluginHost.Load();
        NodeCatalog.Install(plugins.Modules);

        using var session = HeadlessUnitTestSession.StartNew(typeof(ShotApp), AvaloniaTestIsolationLevel.PerAssembly);

        return session.Dispatch(() => TakeAsync(request, plugins, error), CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Takes the shot. Runs on the UI thread of an application in the editor's theme.</summary>
    internal static async Task<int> TakeAsync(ShotRequest request, PluginCatalog plugins, TextWriter error)
    {
        var preset = request.Preset is { } named
            ? plugins.Presets.FirstOrDefault(p => string.Equals(p.Name, named, StringComparison.OrdinalIgnoreCase))
            : null;

        if (request.Preset is not null && preset is null)
        {
            error.WriteLine($"Flyback {ShotRequest.Flag}: no preset is called '{request.Preset}'.");
            return Failed;
        }

        var sound = new ShotSound();

        var provider = EditorServices.Provider(new EditorSetup { Plugins = plugins }, services =>
        {
            services.AddSingleton<IAudioEngine>(sound);
            services.AddSingleton(new AudioSetup(new SilentAudioDevice(), plugins.PreferredAudioOutput));
            services.AddSingleton(sp => new OutputSettingRepository(sp.GetRequiredService<EditorSetup>())
            {
                Current = new OutputSettings { Gpu = false, DefaultPreset = preset?.Name ?? string.Empty },
            });
        });

        var window = provider.Window();

        try
        {
            window.Start();
            window.Width = request.Width;
            window.Height = request.Height;
            window.Show();
            Settle(window);

            if (request.Patch is { } path && !await OpenAsync(provider, path, error)) return Failed;

            var playback = provider.GetRequiredService<Playback>();
            await Until(() => !playback.Starting, "the patch to compile", window);

            var canvas = provider.GetRequiredService<NodeEditor>();
            canvas.View.FrameAll();
            Settle(window);

            if (request.Select is { } name && !Select(canvas, name, error)) return Failed;

            if (playback.HasPicture) await RunUpTo(request.At, provider.GetRequiredService<PreviewHost>(), sound, playback, window);
            else playback.SeekTo(request.At);

            provider.GetRequiredService<StatusBar>().Update();
            Settle(window);

            using var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The window drew nothing to capture.");

            frame.Save(request.Out, new PngBitmapEncoderOptions());

            return Ok;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error.WriteLine($"Flyback {ShotRequest.Flag}: {ex.Message}");
            return Failed;
        }
        finally
        {
            window.CloseWithoutAsking();
        }
    }

    /// <summary>Opens <paramref name="path"/> as a launch does, and says why where it would not open.</summary>
    private static async Task<bool> OpenAsync(ServiceProvider provider, string path, TextWriter error)
    {
        var said = new List<string>();
        var report = provider.GetRequiredService<ReportLine>();
        EventHandler<string> heard = (_, message) => said.Add(message);

        report.Said += heard;

        try
        {
            await provider.GetRequiredService<PatchOpening>().OpenPathAsync(Path.GetFullPath(path));
        }
        finally
        {
            report.Said -= heard;
        }

        if (provider.GetRequiredService<PatchFiles>().Name == Path.GetFileNameWithoutExtension(path)) return true;

        error.WriteLine($"Flyback {ShotRequest.Flag}: {said.LastOrDefault() ?? $"{path} did not open."}");
        return false;
    }

    /// <summary>Selects the box called <paramref name="name"/>, or else the module, as a click on it would.</summary>
    private static bool Select(NodeEditor canvas, string name, TextWriter error)
    {
        var patch = canvas.History.Patch;
        var groups = patch.Groups ?? [];

        if (groups.FirstOrDefault(g => Named(g.Title(), name)) is { } group)
        {
            canvas.Selection.SelectGroup(group);
            return true;
        }

        if (patch.Nodes.FirstOrDefault(n => NodeCatalog.Get(n.TypeId) is { } def && Named(n.Title(def), name)) is { } node)
        {
            canvas.Selection.Select(node.Id);
            return true;
        }

        error.WriteLine($"Flyback {ShotRequest.Flag}: no box or module is called '{name}'. The boxes are:");

        foreach (var box in groups) error.WriteLine($"    {box.Title()}");

        return false;

        static bool Named(string title, string name) => string.Equals(title, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Plays the picture up to <paramref name="at"/> a frame at a time, each drawn before the
    /// clock moves on, and leaves it held there.
    /// </summary>
    private static async Task RunUpTo(double at, PreviewHost preview, ShotSound sound, Playback playback, MainWindow window)
    {
        var from = Math.Max(0, at - RunUp);

        playback.SeekTo(from);
        preview.Clock = () => sound.Time;

        for (var frame = 0; ; frame++)
        {
            var drawn = preview.Frames;
            sound.Time = Math.Min(from + frame * Step, at);

            await Until(() => preview.Frames > drawn, $"the picture at {sound.Time:0.00} s", window);

            if (sound.Time >= at) return;
        }
    }

    private static async Task Until(Func<bool> done, string what, MainWindow window)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"waited a minute for {what}.");

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            await Task.Delay(5);
        }
    }

    private static void Settle(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
