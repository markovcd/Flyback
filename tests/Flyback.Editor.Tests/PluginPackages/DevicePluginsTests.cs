using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Composition;
using Flyback.Editor.PluginPackages;
using Flyback.Editor.Shell;
using Flyback.Editor.Site;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.PluginPackages;

/// <summary>A phone or tablet has only the plugins linked into it, so it has no plugins window (ADR-0184).</summary>
public sealed class DevicePluginsTests : EditorTest
{
    private static readonly Uri Root = new("https://presets.example/");

    private MainWindow Open(bool onDevice)
    {
        var window = NewMainWindow(new EditorSetup { Host = new() { Place = onDevice ? EditorPlace.Device : EditorPlace.Desktop, PresetSite = Root } });

        window.Show();
        Settle(window);

        return window;
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void Only_a_desktop_has_the_plugins_button(bool onDevice, int buttons) =>
        All<Button>(Open(onDevice)).Count(b => b.Name == "plugins").ShouldBe(buttons);

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Only_a_desktop_asks_the_site_for_plugins(bool onDevice, bool asked)
    {
        var window = Open(onDevice);

        (PluginSite.For(Service<SiteAccess>(window), Service<EditorHost>(window)) is not null).ShouldBe(asked);
    }
}
