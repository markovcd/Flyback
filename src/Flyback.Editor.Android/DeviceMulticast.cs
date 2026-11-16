using Android.Net.Wifi;

namespace Flyback.Editor.Android;

/// <summary>The Wi-Fi multicast lock an Ableton Link session needs on Android, which otherwise drops every multicast packet to save power.</summary>
internal sealed class DeviceMulticast : IDisposable
{
    private readonly WifiManager.MulticastLock? held;

    private DeviceMulticast()
    {
        var wifi = (WifiManager?)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.WifiService);

        held = wifi?.CreateMulticastLock("Flyback Ableton Link");

        if (held is null) return;

        held.SetReferenceCounted(false);
        held.Acquire();
    }

    public static IDisposable Take() => new DeviceMulticast();

    public void Dispose()
    {
        if (held is { IsHeld: true }) held.Release();
    }
}
