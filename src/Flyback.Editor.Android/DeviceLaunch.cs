using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Flyback.Editor.Android;

/// <summary>The Android application, which starts Avalonia on <see cref="DeviceApp"/>.</summary>
[Application]
public sealed class DeviceLaunch(IntPtr handle, JniHandleOwnership ownership) : AvaloniaAndroidApplication<DeviceApp>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();
}
