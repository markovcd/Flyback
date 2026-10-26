using System.Runtime.InteropServices;
using Avalonia;
using Flyback.Host;
using Flyback.Specs.Support;
using Flyback.Ui;
using Flyback.Ui.Controls;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// What macOS is drawn through: what the editor and the viewer hand Avalonia for the
/// window, and the surface the picture is drawn on for the setting.
/// </summary>
/// <param name="settings">The scenario's settings, which <see cref="DriverSteps"/> keeps.</param>
[Binding]
public sealed class MacDriverSteps(DriverSteps settings)
{
    private IReadOnlyList<AvaloniaNativeRenderingMode> tried = [];
    private string? drawnThrough;

    [Given("the settings ask the Mac for OpenGL")]
    public void GivenOpenGl() => new OutputSettings { Graphics = GraphicsDriver.OpenGl }.Save(settings.Path);

    [When("Flyback starts on macOS")]
    public void WhenStarted() => tried = GraphicsDrivers.MacModes;

    /// <summary>The surface the Picture section asks the preview for, from the setting it reads.</summary>
    [When("a Mac draws the picture")]
    public void WhenDrawn() => Headless.Run(() =>
        drawnThrough = PreviewHost.Surface(OutputSettings.Load(settings.Path).DriverOf(OutputSettings.DriversOn(OSPlatform.OSX))) switch
        {
            MetalPreviewSurface => GraphicsApi.Metal,
            GpuPreviewSurface => "OpenGL",
            var other => other.GetType().Name,
        });

    [Then("it draws through OpenGL first")]
    public void ThenOpenGl() => tried[0].ShouldBe(AvaloniaNativeRenderingMode.OpenGl);

    [Then("it draws through Metal where OpenGL will not start")]
    public void ThenFallsBack() => tried[1].ShouldBe(AvaloniaNativeRenderingMode.Metal);

    [Then("the picture is drawn through {string}")]
    public void ThenPictureThrough(string api) => drawnThrough.ShouldBe(api);
}
