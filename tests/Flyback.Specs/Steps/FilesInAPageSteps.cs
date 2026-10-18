using Flyback.Core.Graph;
using Flyback.Editor.Shell;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Specs.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>A file a page's picker hands the module panel, and the bundle View it hands on.</summary>
[Binding]
public sealed class FilesInAPageSteps(EditorDriver editor, PatchContext context, HandedViewer viewer)
{
    private readonly BrowserPicker picker = new();

    [Given("a MIDI File with no file chosen")]
    public void GivenAnEmptyMidiFile()
    {
        context.Add("song", NodeCatalog.MidiFileTypeId);
        context.Add("screen", "output");
        context.Wire("song", "gate", "screen", "left");
        context.SetInput("screen", "volume", 1f);

        editor.Services += services => services.AddSingleton<IFilePickers>(picker);
    }

    [When("a MIDI file of a note held for half a second is chosen for it from the browser")]
    public void WhenChosen()
    {
        picker.Ready = BrowserFile.Of("tune.mid", MidiFiles.Bytes((60, 0, 0.5)));

        editor.Select(context.Node("song").Id);
        editor.PressInRow("midi file", "chooseFile");
    }

    [Then("the MIDI File's panel names {string}")]
    public void ThenNamed(string name)
    {
        editor.Select(context.Node("song").Id);
        editor.PanelRow("midi file").ShouldBe($"{name} Choose…");
    }

    [When("the patch plays from the bundle the viewer was handed")]
    public void WhenPlaysFromBundle()
    {
        var (_, bundle) = viewer.Handed.ShouldNotBeNull();
        var read = PatchBundle.Read(new MemoryStream(bundle));

        context.Replace(read.Patch);
        context.Files = BundleFiles.Of(read);
    }
}
