using Avalonia.Input;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The modules over a full-screen picture, for playing a patch live.</summary>
[Binding]
public sealed class LivePatchingSteps(EditorDriver editor)
{
    [When("Shift+F11 is pressed")]
    public void WhenShiftF11() => editor.Press(PhysicalKey.F11, RawInputModifiers.Shift);

    [When("Tab is pressed over the picture")]
    public void WhenTab() => editor.Press(PhysicalKey.Tab);

    [When("F2 is pressed over the picture")]
    public void WhenF2() => editor.Press(PhysicalKey.F2);

    [Then("the patch's text stands over the picture")]
    public void ThenTextOver() => editor.TextOverPicture.ShouldBeTrue();

    [When("Escape is pressed over the picture")]
    public void WhenEscape() => editor.Press(PhysicalKey.Escape);

    [Then("the picture has the whole window")]
    public void ThenFullScreen() => editor.PictureFullScreen.ShouldBeTrue();

    [Then("the modules stand over the picture")]
    public void ThenModulesOver() => editor.ModulesOverPicture.ShouldBeTrue();

    [Then("the picture has the whole window to itself")]
    public void ThenAlone()
    {
        editor.PictureFullScreen.ShouldBeTrue();
        editor.ModulesOverPicture.ShouldBeFalse();
    }

    [Then("the modules are back on the canvas")]
    public void ThenBack()
    {
        editor.ModulesOverPicture.ShouldBeFalse();
        editor.CanvasInItsPane.ShouldBeTrue();
    }
}
