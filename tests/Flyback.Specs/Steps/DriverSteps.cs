using Avalonia;
using Flyback.App;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>What Windows is drawn through: the setting, and what the editor and the viewer hand Avalonia for it.</summary>
[Binding]
public sealed class DriverSteps : IDisposable
{
    /// <summary>Where a scenario keeps its settings, so the machine's own are never touched.</summary>
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-driver-specs");

    private string Path => System.IO.Path.Combine(folder.FullName, "settings.json");

    private IReadOnlyList<Win32RenderingMode> tried = [];

    [Given("no settings have been saved")]
    public void GivenNoSettings() => File.Exists(Path).ShouldBeFalse();

    [Given("the settings ask for Direct3D")]
    public void GivenDirect3D() => new OutputSettings { Driver = GraphicsDriver.Direct3D }.Save(Path);

    /// <summary>What both programs' <c>Main</c> reads and hands Avalonia before it opens a window.</summary>
    [When("Flyback starts on Windows")]
    public void WhenStarted() => tried = GraphicsDrivers.Modes(OutputSettings.Load(Path).Driver);

    [Then("it draws through OpenGL")]
    public void ThenOpenGl() => tried[0].ShouldBe(Win32RenderingMode.Wgl);

    [Then("it draws through Direct3D where OpenGL will not start")]
    public void ThenFallsBack() => tried[1].ShouldBe(Win32RenderingMode.AngleEgl);

    [Then("it draws through Direct3D")]
    public void ThenDirect3D() => tried[0].ShouldBe(Win32RenderingMode.AngleEgl);

    [Then("it never tries OpenGL")]
    public void ThenNeverOpenGl() => tried.ShouldNotContain(Win32RenderingMode.Wgl);

    public void Dispose() => folder.Delete(recursive: true);
}
