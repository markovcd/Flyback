using Reqnroll;
using Shouldly;
using Flyback.App;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Flyback.App.Files;

namespace Flyback.Specs.Steps;

/// <summary>
/// What the editor does about work nobody saved: the question it asks before a patch
/// is replaced, and what it puts back after a crash.
/// </summary>
[Binding]
public sealed class UnsavedWorkSteps(PatchContext context, Editor editor) : IDisposable
{
    private readonly DirectoryInfo recovery = Directory.CreateTempSubdirectory("flyback-recovery-specs");

    [When("the preset {string} is picked")]
    public void WhenAPresetIsPicked(string name) => editor.PickPreset(name);

    [When("the question is answered {string}")]
    public void WhenAnswered(string label) => editor.Answer(label);

    [Then("the editor asks about unsaved changes")]
    public void ThenItAsks() => editor.Asking.ShouldNotBeNull("nothing was asked").ShouldContain("not been saved");

    [Then("the editor shows the preset {string}")]
    public void ThenShowing(string name) => editor.Showing.ShouldBe(name);

    /// <summary>What a crash leaves: the snapshot the window kept, and nobody holding it.</summary>
    [Given("Flyback stopped without closing, holding that patch unsaved as {string}")]
    public void GivenACrash(string name)
    {
        var crashed = Recovery.Open(recovery.FullName).ShouldNotBeNull();

        crashed.Keep(new RecoveredWork(name, Beside: null, PatchIO.ToJson(context.Patch), Source: null, Conversation: null, Files: null));
        crashed.Abandon();
    }

    [When("the editor starts again")]
    public void WhenStartedAgain()
    {
        editor.Setup = new EditorSetup { RecoveryFolder = recovery.FullName };
        editor.OnThePatch = false;
        editor.Open();
    }

    [Then("the editor holds {string}, unsaved")]
    public void ThenHolds(string name)
    {
        editor.Title.ShouldStartWith(name);
        editor.Unsaved.ShouldBeTrue();
    }

    [Then("the editor says it restored {string}")]
    public void ThenSaysRestored(string name) => editor.Reported.ShouldContain(line => line.StartsWith($"Restored {name}", StringComparison.Ordinal));

    public void Dispose()
    {
        // Closed first, so the window has let go of its lock on the folder.
        editor.Dispose();
        recovery.Delete(recursive: true);
    }
}
