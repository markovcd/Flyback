using System.Diagnostics;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Plugins.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flyback.Viewer;

/// <summary>
/// The viewer's composition root: one run's player, its window and what they play
/// through, registered in one container, as the editor's are (ADR-0150).
/// </summary>
/// <remarks>
/// A run is one container, so everything is a singleton of it. The sound device and
/// the MIDI backend were opened before Avalonia started and are handed in on the
/// launch. The container holds a null for a run with no MIDI backend, and a silent
/// device for one with no sound.
/// </remarks>
internal static class ViewerServices
{
    /// <summary>The window <paramref name="launch"/> plays in, with any registration <paramref name="replace"/> swaps.</summary>
    public static ViewerWindow Window(ViewerLaunch launch, Action<IServiceCollection>? replace = null) =>
        Build(launch, replace).GetRequiredService<ViewerWindow>();

    /// <summary>The player alone, for a run with no window and so no picture.</summary>
    public static ViewerPlayer Player(ViewerLaunch launch, Action<IServiceCollection>? replace = null) =>
        Build(launch, services =>
        {
            services.AddSingleton(_ => (PreviewHost)null!);
            replace?.Invoke(services);
        }).GetRequiredService<ViewerPlayer>();

    public static IServiceCollection AddViewer(this IServiceCollection services, ViewerLaunch launch)
    {
        services.AddSingleton(launch);
        services.AddSingleton<IIlCompilerSetup>(launch.Options);

        // Played time is counted on the wall clock; a test hands over one it moves itself.
        services.TryAddSingleton<Func<TimeSpan>>(_ => Watch());

        services.AddSingleton(_ => launch.Instruments!);

        services.AddSingleton<IlCompiler>();

        services.AddSingleton(_ => launch.Device ?? new SilentAudioDevice());
        services.AddSingleton<AudioEngine>();

        services.AddSingleton<MidiHub>();
        services.AddSingleton<ControlHub>();

        // A surface only for a picture there is a window to show, since one in the tree renders on a timer.
        services.AddSingleton(_ => launch.Pictured ? new PreviewHost() : null!);

        services.AddSingleton<ViewerPlayer>();
        services.AddSingleton<ViewerWindow>();

        return services;
    }

    private static ServiceProvider Build(ViewerLaunch launch, Action<IServiceCollection>? replace)
    {
        var services = new ServiceCollection().AddViewer(launch);
        replace?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static Func<TimeSpan> Watch()
    {
        var watch = Stopwatch.StartNew();

        return () => watch.Elapsed;
    }
}
