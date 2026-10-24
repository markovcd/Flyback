using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Flyback.Editor.Composition;

namespace Flyback.Editor.Android;

/// <summary>The editor in the Android app (ADR-0184), in the app's private folder.</summary>
public sealed class DeviceApp : Avalonia.Application
{
    public override void Initialize() => EditorTheme.Apply(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime device)
            throw new NotSupportedException("The Android editor runs in an activity.");

        // The activity reads its intent after the application starts, so the editor waits for the activity.
        var host = new Decorator();
        host.AttachedToVisualTree += (_, _) =>
        {
            if (host.Child is not null) return;

            var request = DeviceRequest.Current;
            host.Child = DeviceEditor.Build(typeof(DeviceApp).Assembly, DeviceFolders.Private(), request.Preset, request.Interpreted, provider => DeviceState.Provider = provider);
        };

        device.MainView = host;

        base.OnFrameworkInitializationCompleted();
    }
}
