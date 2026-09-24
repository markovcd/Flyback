using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Flyback.App.Tests.Ui;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Viewer;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.App.Tests.Viewer;

/// <summary>
/// The container a viewer run is composed in: everything it registers can be built,
/// a surface is made only for a picture there is a window to show, and a run with
/// no window plays without one.
/// </summary>
public class ViewerServicesTests : UiTest
{
    private static ViewerLaunch Launch(ViewerOptions options) => new(
        new Opened(Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn), new SampleLibrary(), new ImageLibrary()),
        null,
        options);

    private static ViewerOptions Options() => new() { Gpu = false, Size = new PixelSize(320, 180) };

    [AvaloniaFact]
    public void Every_service_the_viewer_registers_can_be_built()
    {
        var services = new ServiceCollection().AddViewer(Launch(Options()));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        foreach (var registered in services)
            Should.NotThrow(() => provider.GetService(registered.ServiceType), registered.ServiceType.Name);

        provider.GetRequiredService<ViewerPlayer>().Dispose();
    }

    [AvaloniaFact]
    public void A_window_is_handed_the_player_and_the_surface_the_container_made()
    {
        var provider = new ServiceCollection().AddViewer(Launch(Options())).BuildServiceProvider();

        var window = Owned(provider.GetRequiredService<ViewerWindow>());

        window.Player.ShouldBeSameAs(provider.GetRequiredService<ViewerPlayer>());
        window.Preview.ShouldNotBeNull().ShouldBeSameAs(provider.GetService<PreviewHost>());
    }

    [AvaloniaFact]
    public void A_run_with_no_picture_to_show_makes_no_surface()
    {
        new ServiceCollection().AddViewer(Launch(Options() with { NoVideo = true }))
            .BuildServiceProvider()
            .GetService<PreviewHost>()
            .ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_player_on_its_own_plays_without_a_surface()
    {
        using var player = ViewerServices.Player(Launch(Options()));

        player.Time.ShouldBe(player.Audio.Time);
    }
}
