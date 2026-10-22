using Foundation;
using UIKit;

namespace Flyback.Editor.iOS;

/// <summary>Starts UIKit on <see cref="DeviceLaunch"/>, after reading what the launch asked for.</summary>
public static class Program
{
    public static void Main(string[] args)
    {
        // Main is handed none of what simctl launch passed; the process info has them.
        DeviceRequest.Take(NSProcessInfo.ProcessInfo.Arguments);
        UIApplication.Main(args, null, typeof(DeviceLaunch));
    }
}
