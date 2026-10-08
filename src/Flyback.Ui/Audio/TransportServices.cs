using Flyback.Engine.Compile;
using Flyback.Ui.Midi;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.Ui.Audio;

/// <summary>What a patch plays through, registered once for the editor and the viewer alike (ADR-0150).</summary>
/// <remarks>
/// The container that adds it supplies what differs between them: the <see cref="AudioSetup"/>,
/// the <see cref="Plugins.Midi.IMidiInput"/> MIDI is heard through, and optionally the
/// <see cref="IIlCompilerSetup"/>, a <see cref="Controls.PreviewHost"/> and a <see cref="LineIn"/>.
/// </remarks>
internal static class TransportServices
{
    public static IServiceCollection AddTransport(this IServiceCollection services)
    {
        services.AddSingleton<IlCompiler>();

        services.AddSingleton<AudioEngine>();
        services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<AudioEngine>());

        // Nothing is opened by this: the backend is asked for a device only once a compiled program is reading one.
        services.AddSingleton<MidiHub>();
        services.AddSingleton<ControlHub>();

        services.AddSingleton<Transport>();

        return services;
    }
}
