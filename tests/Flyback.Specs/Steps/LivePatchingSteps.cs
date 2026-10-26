using Avalonia.Input;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The modules over a full-screen picture, for playing a patch live.</summary>
[Binding]
public sealed class LivePatchingSteps(PatchContext context, EditorDriver editor)
{
    [Then("the sine's fill lets the picture through")]
    public void ThenSeeThrough() => editor.FillOpacity(context.Node("sine").Id).ShouldBeLessThan(1);

    [Then("the sine's fill is solid")]
    public void ThenSolid() => editor.FillOpacity(context.Node("sine").Id).ShouldBe(1);

    [When("Shift+F11 is pressed")]
    public void WhenShiftF11() => editor.Press(PhysicalKey.F11, RawInputModifiers.Shift);

    [When("the Live button is pressed")]
    public void WhenLiveButton() => editor.PressPanelButton("live");

    [When("the text button over the picture is pressed")]
    public void WhenTextButton() => editor.PressPanelButton("liveText");

    [When("the leave button over the picture is pressed")]
    public void WhenLeaveButton() => editor.PressPanelButton("liveLeave");

    [When("Tab is pressed over the picture")]
    public void WhenTab() => editor.Press(PhysicalKey.Tab);

    [When("F2 is pressed over the picture")]
    public void WhenF2() => editor.Press(PhysicalKey.F2);

    [Then("the patch's text stands over the picture")]
    public void ThenTextOver() => editor.TextOverPicture.ShouldBeTrue();

    [When("the patch over the picture is left alone")]
    public void WhenLeftAlone() => editor.LeaveLivePatchAlone();

    [Then("the patch over the picture has faded")]
    public void ThenFaded() => editor.LivePatchFaded.ShouldBeTrue();

    [Then("the patch over the picture is back")]
    public void ThenNotFaded() => editor.LivePatchFaded.ShouldBeFalse();

    [When("a key is typed in the text")]
    public void WhenKeyTyped() => editor.Press(PhysicalKey.ArrowRight);

    [When("the caret is put in the text")]
    public void WhenCaretInText() => editor.ClickIntoText();

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
