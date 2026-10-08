using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace Flyback.Editor.Android;

/// <summary>The one activity: the editor, full screen, kept through a rotation.</summary>
[Activity(
    Label = "Flyback",
    Theme = "@style/Flyback.Theme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        DeviceRequest.Take(Intent);
        base.OnCreate(savedInstanceState);
    }
}
