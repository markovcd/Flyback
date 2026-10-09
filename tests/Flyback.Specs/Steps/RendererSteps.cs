using Flyback.Ui.Controls;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>What the status bar and the stats line call whatever draws the picture.</summary>
[Binding]
public sealed class RendererSteps
{
    private string? named;

    /// <summary>The renderer string each driver reports, and whether its context is OpenGL ES.</summary>
    private static readonly Dictionary<string, (bool Embedded, string Renderer)> Drivers = new()
    {
        ["desktop OpenGL"] = (false, "NVIDIA GeForce RTX 4070 SUPER/PCIe/SSE2"),
        ["ANGLE over Direct3D 11"] = (true, "ANGLE (NVIDIA, NVIDIA GeForce RTX 4070 SUPER Direct3D11 vs_5_0 ps_5_0, D3D11)"),
        ["ANGLE over Vulkan"] = (true, "ANGLE (Intel, Vulkan 1.3.0 (Intel(R) UHD Graphics 620))"),
        ["OpenGL ES, straight from Mesa"] = (true, "Mesa Intel(R) UHD Graphics 620"),
    };

    [Given("a graphics card driven through {}")]
    public void GivenCard(string driver)
    {
        var (embedded, renderer) = Drivers[driver];
        named = GraphicsApi.Name(embedded, renderer);
    }

    [Given("the picture is drawn on the processor")]
    public void GivenProcessor() => named = GraphicsApi.Processor;

    [Then("the picture is said to be drawn through {}")]
    public void ThenNamed(string name) => named.ShouldBe(name);
}
