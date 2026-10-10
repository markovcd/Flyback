using Android.Content;
using Android.Content.PM;
using Android.Media.Midi;
using Android.Runtime;
using Flyback.Plugins.Midi;

namespace Flyback.Plugins.AndroidIO;

/// <summary>
/// Offers the backend without opening anything: whatever <c>android.media.midi</c> has
/// plugged in over USB, or offered by another app.
/// </summary>
/// <remarks>
/// A Bluetooth keyboard is not listed: Android lists one only once an app has opened it by
/// its Bluetooth address, which takes a scan and a permission of its own.
/// </remarks>
public sealed class MidiManagerInput : IMidiInput
{
    public string Id => "midimanager";

    public string Name => "Android MIDI";

    /// <summary>The native backends' 100; it is the only one supported on Android.</summary>
    public int Priority => 100;

    /// <summary>Two questions: the operating system, and whether this device has MIDI at all, which not every Android device does.</summary>
    public bool IsSupported => OperatingSystem.IsAndroid() && Manager() is not null;

    /// <summary>What is plugged in right now, asked of the MIDI service each time.</summary>
    /// <remarks>
    /// Total, whatever the device is doing: a service that will not answer is a shorter list,
    /// and there are always the keys on the screen behind it.
    /// </remarks>
    public IReadOnlyList<MidiPortInfo> Ports
    {
        get
        {
            if (Manager() is not { } manager) return [];

            try
            {
                return MidiPorts.Named(Sources(manager).Select(source => source.Name));
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>Opens one device by the id a patch stored, resolved against what is plugged in now.</summary>
    public IMidiPort Open(string port, MidiCallback deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        if (Manager() is not { } manager)
            throw new PlatformNotSupportedException("Android MIDI input needs an Android device with MIDI.");

        // One walk, named once: the ids have to be the ones this very list would have produced.
        var sources = Sources(manager);
        var ports = MidiPorts.Named(sources.Select(source => source.Name));

        for (var index = 0; index < ports.Count; index++)
            if (string.Equals(ports[index].Id, port, StringComparison.Ordinal))
                return new MidiManagerPort(port, manager, sources[index], deliver);

        throw new InvalidOperationException($"'{port}' is not plugged in.");
    }

    /// <summary>The MIDI service, or null where the device has none. Asking opens nothing.</summary>
    private static MidiManager? Manager()
    {
        if (!OperatingSystem.IsAndroid()) return null;

        try
        {
            var context = global::Android.App.Application.Context;

            if (context.PackageManager?.HasSystemFeature(PackageManager.FeatureMidi) != true) return null;

            return context.GetSystemService(Context.MidiService)?.JavaCast<MidiManager>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Every port a device talks from, in the order Android lists the devices and each its
    /// ports. A device with several is listed once per port, and the names are numbered apart.
    /// </summary>
    private static IReadOnlyList<MidiSource> Sources(MidiManager manager)
    {
        var found = new List<MidiSource>();

        foreach (var device in Devices(manager))
        {
            var name = NameOf(device);

            foreach (var port in (device.GetPorts() ?? []).Where(p => p.Type == MidiPortType.Output).OrderBy(p => p.PortNumber))
                found.Add(new MidiSource(device, port.PortNumber, name));
        }

        return found;
    }

    /// <summary>The devices that talk in bytes, which every keyboard does.</summary>
    private static IEnumerable<MidiDeviceInfo> Devices(MidiManager manager)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return manager.GetDevicesForTransport((int)MidiTransport.MidiByteStream) ?? [];

        return manager.GetDevices() ?? [];
    }

    /// <summary>What the device calls itself: its name, or its product where a USB device gives no name.</summary>
    private static string NameOf(MidiDeviceInfo device)
    {
        var properties = device.Properties;

        return properties?.GetString(MidiDeviceInfo.PropertyName)
            ?? properties?.GetString(MidiDeviceInfo.PropertyProduct)
            ?? "";
    }
}
