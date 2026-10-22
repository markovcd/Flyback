using UIKit;

namespace Flyback.Editor.iOS;

/// <summary>Starts UIKit on <see cref="DeviceLaunch"/>, after reading what the launch asked for.</summary>
public static class Program
{
    public static void Main(string[] args)
    {
        DeviceRequest.Take(args);
        UIApplication.Main(args, null, typeof(DeviceLaunch));
    }
}
