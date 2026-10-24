using Avalonia.Controls.ApplicationLifetimes;
using Flyback.Editor.Composition;

namespace Flyback.Editor.iOS;

/// <summary>The editor on an iPhone or iPad, in the app's Application Support folder.</summary>
public sealed class DeviceApp : Avalonia.Application
{
    public override void Initialize() => EditorTheme.Apply(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime device)
            throw new NotSupportedException("The iOS editor runs in a single view.");

        var request = DeviceRequest.Current;
        device.MainView = DeviceEditor.Build(typeof(DeviceApp).Assembly, DeviceFolders.Private(), request.Preset, request.Interpreted, DeviceState.Listen);

        base.OnFrameworkInitializationCompleted();
    }
}
