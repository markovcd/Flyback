using Avalonia;
using Flyback.Ui;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>What macOS is drawn through: what the editor and the viewer hand Avalonia.</summary>
[Binding]
public sealed class MacDriverSteps
{
    private IReadOnlyList<AvaloniaNativeRenderingMode> tried = [];

    [When("Flyback starts on macOS")]
    public void WhenStarted() => tried = GraphicsDrivers.MacModes;

    [Then("it draws through OpenGL first")]
    public void ThenOpenGl() => tried[0].ShouldBe(AvaloniaNativeRenderingMode.OpenGl);

    [Then("it draws through Metal where OpenGL will not start")]
    public void ThenFallsBack() => tried[1].ShouldBe(AvaloniaNativeRenderingMode.Metal);
}
