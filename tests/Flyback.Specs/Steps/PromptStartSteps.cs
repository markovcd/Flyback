using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// The preset gallery's card for starting a new patch from a typed idea, and the assistant's
/// box writing a message out before it is sent, driven through the open window.
/// </summary>
[Binding]
public sealed class PromptStartSteps(EditorDriver editor, PatchContext context) : IDisposable
{
    private readonly BriefingAssistant assistant = new();

    [Given("no assistant is set up")]
    public void GivenNoAssistant() => editor.Setup = editor.Setup with { Plugins = PluginCatalog.Empty };

    private readonly AssistantSettings settings = new();

    [Given("an assistant is set up")]
    public void GivenAnAssistant()
    {
        var folders = editor.Setup.Folders;

        settings.Provider = assistant.Id;

        editor.Setup = editor.Setup with
        {
            Plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], [assistant]),
        };

        editor.Services = services => services.AddSingleton(new AssistantSettingRepository(folders, settings));
    }

    [Given("ideas are written out by {string}")]
    public void GivenAnIdeasModel(string model) =>
        settings.Choices[assistant.Id] = new Dictionary<string, string> { [AssistantSchema.IdeasModelKey] = model };

    [When("{string} is typed in the prompt card")]
    public void WhenAnIdeaIsTyped(string idea) => editor.TypePrompt(idea);

    [When("a patch is started from the prompt")]
    public void WhenAPatchIsStarted() => editor.StartPrompt(() => assistant.Heard.Any(heard => !Writing(heard)));

    [When("the assistant's column is opened")]
    public void WhenTheColumnIsOpened() => editor.Toggle("assistant", on: true);

    [When("{string} is typed in the assistant's box")]
    public void WhenAMessageIsTyped(string message) => editor.TypeMessage(message);

    [When("the message is expanded")]
    public void WhenTheMessageIsExpanded() => editor.ExpandMessage();

    [When("the message is expanded again")]
    public void WhenTheMessageIsExpandedAgain() => editor.ExpandMessageOnceDone();

    [Then("the assistant's box still holds the assistant's detailed brief")]
    public void ThenTheBoxStillHoldsTheBrief() => editor.MessageText.ShouldBe(BriefingAssistant.Brief);

    [Then("the assistant's box holds the assistant's detailed brief")]
    public void ThenTheBoxHoldsTheBrief() => editor.MessageText.ShouldBe(BriefingAssistant.Brief);

    [Then("the assistant was asked to write out a change to the patch")]
    public void ThenAskedForAChange() =>
        assistant.Heard.Where(Writing).ShouldHaveSingleItem().ShouldContain("request to change it");

    [Then("the assistant was asked to write out a new patch")]
    public void ThenAskedForANewPatch() =>
        assistant.Heard.Where(Writing).ShouldHaveSingleItem().ShouldContain("short idea for a new patch");

    [Then("nothing has been sent to build from")]
    public void ThenNothingSent() => assistant.Heard.ShouldAllBe(heard => Writing(heard));

    [Then("the gallery has no card to start from a prompt")]
    public void ThenNoCard() => editor.GalleryOffersPrompt.ShouldBeFalse();

    [Then("the prompt card holds the assistant's detailed brief")]
    public void ThenTheBriefIsThere() => editor.PromptText.ShouldBe(BriefingAssistant.Brief);

    [Then("the canvas holds only the Output")]
    public void ThenOnlyTheOutput() =>
        context.Patch.Nodes.ShouldHaveSingleItem().TypeId.ShouldBe(NodeCatalog.OutputTypeId);

    [Then("the assistant's column is open")]
    public void ThenTheColumnIsOpen() => editor.AssistantColumnOpen.ShouldBeTrue();

    [Then("the transcript says the idea was written out first")]
    public void ThenTheTranscriptSaysSo() =>
        editor.TranscriptLines.ShouldContain(line => line.Contains("written out as the brief below"));

    [Then("the assistant was asked to write the idea out once")]
    public void ThenWrittenOutOnce() => assistant.Heard.Count(Writing).ShouldBe(1);

    [Then("the assistant has been sent the brief")]
    public void ThenTheBriefWasSent() =>
        assistant.Heard.Where(heard => !Writing(heard)).ShouldHaveSingleItem().ShouldEndWith(BriefingAssistant.Brief);

    [Then("the idea was written out by {string} and the patch built by {string}")]
    public void ThenTheModels(string writer, string builder) => assistant.Models.ShouldBe([writer, builder]);

    /// <summary>Whether a message to the assistant is the request to write an idea out.</summary>
    private static bool Writing(string heard) => heard.Contains("build nothing");

    public void Dispose() => assistant.Dispose();
}
