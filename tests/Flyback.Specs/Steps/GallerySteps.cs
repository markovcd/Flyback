using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The preset gallery's cards, its left column and the chosen card, driven through the open window.</summary>
[Binding]
public sealed class GallerySteps(EditorDriver editor, PatchContext context)
{
    [When("the {string} card is chosen")]
    public void WhenACardIsChosen(string name) => editor.ChooseCard(name);

    [When("the chosen card is used")]
    public void WhenTheChosenCardIsUsed() => editor.UseChosenCard();

    [When("the gallery's {string} row is pressed")]
    public void WhenARowIsPressed(string label) => editor.PressGalleryRow(label);

    [Then("the gallery describes {string}")]
    public void ThenTheGalleryDescribes(string name) => editor.Described.ShouldBe(name);

    [Then("the canvas still holds the sine")]
    public void ThenTheSineIsStillThere() =>
        editor.CanvasTypes.ShouldContain(NodeCatalog.SineTypeId);

    [Then("the preset on the canvas is {string}")]
    public void ThenThePresetIs(string name)
    {
        editor.Showing.ShouldBe(name);
        context.Patch.Nodes.ShouldNotContain(node => node.TypeId == NodeCatalog.SineTypeId);
    }

    [Then("the gallery shows a card for every preset that is heard, and no other")]
    public void ThenOnlyTheHeard()
    {
        var heard = Presets.All
            .Where(preset => preset.Build(NodeCatalog.BuiltIn).Reaches() is var (picture, sound) && (sound || !picture))
            .Select(preset => preset.Name);

        editor.CardsShown.ShouldBe(heard, ignoreOrder: true);
    }
}
