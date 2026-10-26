using Avalonia;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Host;
using Flyback.Ui.Controls;
using Flyback.Ui.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Viewer.Desktop.Tests;

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

    /// <summary>On a Mac the viewer draws through the driver the editor's settings chose, as the editor does.</summary>
    [AvaloniaTheory]
    [InlineData(GraphicsDriver.OpenGl, typeof(GpuPreviewSurface))]
    [InlineData(GraphicsDriver.Metal, typeof(MetalPreviewSurface))]
    public void On_a_Mac_the_surface_is_the_settings_driver(GraphicsDriver driver, Type surface)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Only a Mac chooses between Metal and OpenGL.");

        var launch = Launch(Options()) with { Settings = new OutputSettings { Graphics = driver } };

        new ServiceCollection().AddViewer(launch).BuildServiceProvider()
            .GetRequiredService<PreviewHost>()
            .Child.ShouldBeOfType(surface);
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
    public void A_player_on_its_own_reports_no_picture()
    {
        using var run = ViewerServices.Player(Launch(Options()));

        var report = run.Player.Report();

        report.Renderer.ShouldBeNull();
        report.Frames.ShouldBe(0);
    }

    [AvaloniaFact]
    public void A_player_on_its_own_plays_without_a_surface()
    {
        using var run = ViewerServices.Player(Launch(Options()));

        run.Player.Time.ShouldBe(run.Player.Audio.Time);
    }
}
