using Flyback.Core.Graph;
using Flyback.Editor;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The preset gallery's card for starting a new patch from a typed idea, driven through the open window.</summary>
[Binding]
public sealed class PromptStartSteps(EditorDriver editor, PatchContext context) : IDisposable
{
    private readonly BriefingAssistant assistant = new();

    [Given("no assistant is set up")]
    public void GivenNoAssistant() => editor.Setup = editor.Setup with { Plugins = PluginCatalog.Empty };

    [Given("an assistant is set up")]
    public void GivenAnAssistant()
    {
        var folders = editor.Setup.Folders;

        editor.Setup = editor.Setup with
        {
            Plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], [assistant]),
        };

        editor.Services = services => services.AddSingleton(
            new AssistantSettingRepository(folders, new AssistantSettings { Provider = assistant.Id }));
    }

    [When("{string} is typed in the prompt card")]
    public void WhenAnIdeaIsTyped(string idea) => editor.TypePrompt(idea);

    [When("the prompt is expanded")]
    public void WhenThePromptIsExpanded() => editor.ExpandPrompt();

    [When("a patch is started from the prompt")]
    public void WhenAPatchIsStarted() => editor.StartPrompt(() => !assistant.Heard.IsEmpty);

    [Then("the gallery has no card to start from a prompt")]
    public void ThenNoCard() => editor.GalleryOffersPrompt.ShouldBeFalse();

    [Then("the prompt card holds the assistant's detailed brief")]
    public void ThenTheBriefIsThere() => editor.PromptText.ShouldBe(BriefingAssistant.Brief);

    [Then("the canvas holds only the Output")]
    public void ThenOnlyTheOutput() =>
        context.Patch.Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);

    [Then("the assistant's column is open")]
    public void ThenTheColumnIsOpen() => editor.AssistantColumnOpen.ShouldBeTrue();

    [Then("the assistant has been sent {string}")]
    public void ThenTheAssistantWasSent(string prompt) =>
        assistant.Heard.ShouldHaveSingleItem().ShouldEndWith(prompt);

    public void Dispose() => assistant.Dispose();
}
