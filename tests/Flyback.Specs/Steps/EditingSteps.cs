using System.IO.Compression;
using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Core.Language;
using Flyback.Core.Render;
using Flyback.Specs.Support;

using Flyback.Cli.Common;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>Saving, opening, writing out as text, undoing and pasting.</summary>
[Binding]
public sealed class EditingSteps(PatchContext context, Session session, Editor editor)
{
    private const string Stranger = "module.from.the.future";

    // --- files ----------------------------------------------------------------

    [Given("a saved rainbow that also names a module from a newer Flyback")]
    public void GivenAFileWithAStranger()
    {
        Rainbow();
        context.Add("stranger", "pattern.clouds");
        session.Before = context.Render();
        session.File = PatchIO.ToJson(context.Patch).Replace("\"pattern.clouds\"", $"\"{Stranger}\"");
    }

    [Given("a rainbow saved in a file layout newer than this build reads")]
    public void GivenAFileFromTheFuture()
    {
        Rainbow();
        var json = PatchIO.ToJson(context.Patch);
        var layout = $"\"Version\": {PatchIO.FormatVersion}";

        json.ShouldContain(layout);
        session.File = json.Replace(layout, $"\"Version\": {PatchIO.FormatVersion + 1}");
    }

    /// <summary>
    /// A file as it would have read before ADR-0128 moved the filter into the
    /// engine: the same patch, with its type id swapped back to what a build from
    /// before that ADR would have written.
    /// </summary>
    [Given("a {float} Hz sine through a filter, saved under the filter's old id {string}")]
    public void GivenAFilteredSineSavedUnderAnOldId(float frequency, string oldId)
    {
        FilteredSine(frequency);
        session.BeforeSound = Played();

        var json = PatchIO.ToJson(context.Patch);
        json.ShouldContain($"\"{NodeCatalog.FilterTypeId}\"");
        session.File = json.Replace($"\"{NodeCatalog.FilterTypeId}\"", $"\"{oldId}\"");
    }

    [Then("it sounds exactly as it did before it was saved")]
    public void ThenItSoundsAsItDid() => Played().ShouldBe(session.BeforeSound.ShouldNotBeNull());

    [When("the patch is saved and opened again")]
    public void WhenSavedAndOpened()
    {
        session.Before = context.Render();
        session.File = PatchIO.ToJson(context.Patch);
        WhenTheFileIsOpened();
    }

    [When("the file is opened")]
    public void WhenTheFileIsOpened()
    {
        session.Opened = PatchIO.Read(session.File.ShouldNotBeNull());
        context.Replace(session.Opened.Patch);
    }

    [Then("it opens complete")]
    public void ThenComplete() => Opened.IsComplete.ShouldBeTrue(Opened.Summary);

    [Then(@"^it opens with a note that it (.+)$")]
    public void ThenANote(string note) => Opened.Summary.ShouldStartWith($"This patch {note}");

    [Then("the picture is as it was")]
    public void ThenThePictureIsUnchanged() =>
        context.Render().Buffer.ShouldBe(session.Before.ShouldNotBeNull().Buffer);

    // --- text -----------------------------------------------------------------

    [Given("the text:")]
    public void GivenTheText(string source) => Read(source);

    [When("the patch is written out as text and read back")]
    public void WhenWrittenOutAndReadBack()
    {
        session.Before = context.Render();
        Read(PatchPrinter.Print(context.Patch));
        Text.Ok.ShouldBeTrue(Text.Report);
    }

    [Then("the patch says it is {string}")]
    public void ThenDescribedAs(string description) => context.Patch.Description.ShouldBe(description);

    [Then("the patch says it was made by {string}")]
    public void ThenCredited(string author) => context.Patch.Author.ShouldBe(author);

    [Then("the patch plays for {float} seconds")]
    public void ThenLasts(double seconds) => (context.Patch.Length ?? Patch.DefaultLength).ShouldBe(seconds, 0.001);

    [Then("the patch is tagged {string}")]
    public void ThenTagged(string tags) => context.Patch.Tags.ShouldBe(tags.Split(", "));

    [When("the preset {string} is printed from the command line")]
    public void WhenAPresetIsPrinted(string name)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = Cli.Program.Run(
            ["print", "--preset", name],
            new PluginRegistry(() => Plugins.Hosting.PluginCatalog.Empty, "nowhere", null),
            new System.CommandLine.InvocationConfiguration { Output = output, Error = error });

        code.ShouldBe(Exit.Ok, error.ToString());
        Read(output.ToString());
    }

    /// <summary>What the text view does when the hand comes off a panel knob.</summary>
    [When("the panel knob {string} is left at {float}")]
    public void WhenAPanelKnobIsLeft(string name, float value)
    {
        var knob = context.Patch.Controls.ShouldNotBeNull().Single(control => control.Name == name);
        var change = Text.Map.Knob(knob.Id, PatchPrinter.PanelKnob, PatchPrinter.Knob(value, PortDisplay.Number)).ShouldNotBeNull();

        Read(Text.Source[..change.Offset] + change.Text + Text.Source[(change.Offset + change.Length)..]);
    }

    [Given("its modules were never named")]
    public void GivenNeverNamed()
    {
        foreach (var node in context.Patch.Nodes) node.Rename(NodeCatalog.BuiltIn.Require(node.TypeId), null);
    }

    [Then("the text has the line {string}")]
    public void ThenTheTextHasTheLine(string line) =>
        Text.Source.Split('\n').Select(l => l.TrimEnd('\r')).ShouldContain(line, Text.Source);

    [Then("it reads without complaint")]
    public void ThenReadsCleanly() => Text.Ok.ShouldBeTrue(Text.Report);

    [Then("it reads with a warning on line {int}")]
    public void ThenReadsWithAWarning(int line)
    {
        Text.Ok.ShouldBeTrue(Text.Report);

        var issue = Text.Issues.ShouldHaveSingleItem(Text.Report);
        issue.IsError.ShouldBeFalse();
        issue.Line.ShouldBe(line);
    }

    [Then("the patch has no groups")]
    public void ThenNoGroups() => (context.Patch.Groups?.Count ?? 0).ShouldBe(0);

    /// <summary>The line is quoted beside its number, so the reader sees which statement is at fault.</summary>
    [Then("the complaint quotes line {int}")]
    public void ThenTheComplaintQuotes(int line)
    {
        var quoted = Text.Source.Split('\n')[line - 1].TrimEnd('\r');

        Text.Report.ShouldContain($"{line}:");
        Text.Report.ShouldContain(quoted);
    }

    [Then("the panel has a knob {string} resting at {float}")]
    public void ThenThePanelHasAKnob(string name, float value) =>
        context.Patch.Controls.ShouldNotBeNull().ShouldContain(knob => knob.Name == name && Math.Abs(knob.Value - value) < 1e-6f);

    [Then("the patch has a shut group {string} of {int} modules")]
    public void ThenTheGroup(string name, int count) =>
        context.Patch.Groups.ShouldNotBeNull().ShouldContain(group => group.Name == name && group.Collapsed && group.Members.Count == count);

    // --- undo -----------------------------------------------------------------

    [Given("the level is deleted")]
    [When("the level is deleted")]
    public void WhenTheLevelIsDeleted()
    {
        editor.Select(context.Node("level").Id);
        editor.Press(PhysicalKey.Delete);
    }

    [When("that is undone")]
    public void WhenUndone() => editor.PressCtrl(PhysicalKey.Z);

    [When("it is redone")]
    public void WhenRedone() => editor.PressCtrl(PhysicalKey.Y);

    [Then("there is nothing to undo")]
    public void ThenNothingToUndo() => editor.CanUndo.ShouldBeFalse();

    [Then("the patch has unsaved changes")]
    public void ThenModified() => editor.Unsaved.ShouldBeTrue();

    [Then("the patch has no unsaved changes")]
    public void ThenUnmodified() => editor.Unsaved.ShouldBeFalse();

    // --- copy and paste -------------------------------------------------------

    [When("the level and the halving module are copied and pasted")]
    public void WhenThePairIsPasted() => Paste([context.Node("level").Id, context.Node("halve").Id]);

    [When("the Send is put on the bus {string}")]
    public void WhenTheSendIsRenamed(string bus)
    {
        BusEdits.Rename(context.Patch, context.Node("send"), bus);
        context.Replace(context.Patch);
    }

    [When("the Send and its listener are copied and pasted")]
    public void WhenTheBusPairIsPasted() => Paste([context.Node("send").Id, context.Node("receive 1").Id]);

    [When("the listener alone is copied and pasted")]
    public void WhenTheListenerIsPasted() => Paste([context.Node("receive 1").Id]);

    [Then("the copy is on a bus of its own")]
    public void ThenTheCopyHasItsOwnBus()
    {
        var bus = NodeCatalog.BusOf(Pasted(NodeCatalog.SendTypeId)).ShouldNotBeNull();

        bus.ShouldNotBe(NodeCatalog.BusOf(context.Node("send")));
        NodeCatalog.BusOf(Pasted(NodeCatalog.ReceiveTypeId)).ShouldBe(bus);
    }

    [Then("the copy listens to the bus {string}")]
    public void ThenTheCopyListensTo(string bus) =>
        NodeCatalog.BusOf(Pasted(NodeCatalog.ReceiveTypeId)).ShouldBe(bus);

    [When("everything is copied and pasted")]
    public void WhenEverythingIsPasted()
    {
        editor.Open();
        editor.PressCtrl(PhysicalKey.A);
        editor.PressCtrl(PhysicalKey.C);
        editor.PressCtrl(PhysicalKey.V);
    }

    [Then("there are two levels and two halving modules")]
    public void ThenTwoOfEach()
    {
        context.Patch.Nodes.Count(n => n.TypeId == "value").ShouldBe(2);
        context.Patch.Nodes.Count(n => n.TypeId == "math.mul").ShouldBe(2);
    }

    [Then("the pasted halving module is fed by the pasted level")]
    public void ThenThePastedPairIsWired() =>
        context.Patch.IncomingTo(Pasted("math.mul").Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(Pasted("value").Id);

    [Then("the pasted halving module still halves")]
    public void ThenThePastedKnobIsKept() => Pasted("math.mul").InputValues[1].ShouldBe(0.5f);

    [Then("the patch still has one Output")]
    public void ThenOneOutput() => context.Patch.Nodes.Count(n => NodeCatalog.IsSink(n.TypeId)).ShouldBe(1);

    // --- pasting between the canvas and the text ------------------------------

    private const string Grouped = """
        description "somebody else's patch"
        panel level = 0.5

        group "Bass" {
          let tone = sine(freq: 110)
          let quiet = tone * 0.5
        }

        quiet |> out.left
        out.volume = level
        """;

    [When("patch text holding a group of two modules, a panel knob and a description is pasted onto the canvas")]
    public void WhenTextIsPastedOntoTheCanvas()
    {
        editor.Open();
        editor.Clip(Grouped);
        editor.PressCtrl(PhysicalKey.V);

        session.Pasted = editor.Selected;
    }

    [Then("the canvas has those two modules in their group")]
    public void ThenTheGroupArrived()
    {
        session.Pasted.Count.ShouldBe(2);
        session.Pasted.ShouldContain(n => n.TypeId == "osc.sine");

        var group = context.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        group.Name.ShouldBe("Bass");
        group.Members.Order().ShouldBe(session.Pasted.Select(n => n.Id).Order());
    }

    [Then("the patch keeps its own panel and description")]
    public void ThenThePatchKeepsItsOwn()
    {
        context.Patch.Controls.ShouldBeNull();
        context.Patch.Description.ShouldBeNull();
    }

    [Given("the text is the document")]
    public void GivenTheTextIsTheDocument() =>
        editor.ApplyText("""
            let hum = t |> sine(freq: 220)
            hum |> out.left
            """);

    [When("modules copied off a canvas are pasted into the text")]
    public void WhenACopyIsPastedIntoTheText()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var tone = b.Add("osc.sine", 0, 0, (1, 330f));
        var half = b.Add("math.mul", 200, 0, (1, 0.5f));

        // Named as the text already names its own, which the paste must not repeat.
        tone.Name = "hum";
        b.Wire(tone, 0, half, 0);

        editor.Clip(PatchIO.ToJson(b.Patch));
        editor.PasteIntoText();
    }

    [Then("the text builds the pasted modules beside the ones already written")]
    public void ThenTheTextBuildsBoth()
    {
        Read(editor.Text);

        Text.Ok.ShouldBeTrue(Text.Report);
        context.Patch.Nodes.Count(n => n.TypeId == "osc.sine").ShouldBe(2);

        var pasted = context.Patch.Nodes.Single(n => n.TypeId == "osc.sine" && n.InputValues[1] == 330f);
        context.Patch.Connections.Any(c => c.SourceNode == pasted.Id).ShouldBeTrue("the wire out of it came too");
    }

    // --- building blocks ------------------------------------------------------

    private PatchLoad Opened => session.Opened.ShouldNotBeNull("no file has been opened");

    private LanguageLoad Text => session.Text.ShouldNotBeNull("no text has been read");

    private NodeInstance Pasted(string typeId) => session.Pasted.Single(n => n.TypeId == typeId);

    private void Read(string source)
    {
        session.Text = PatchLanguage.Build(source, NodeCatalog.BuiltIn);
        context.Replace(session.Text.Patch);
    }

    /// <summary>Selects the modules in the editor, copies them and pastes them, with the keys.</summary>
    private void Paste(Guid[] ids)
    {
        editor.Select(ids);
        editor.PressCtrl(PhysicalKey.C);
        editor.PressCtrl(PhysicalKey.V);

        // What was pasted is left selected.
        session.Pasted = editor.Selected;
    }

    // --- somebody else's files ------------------------------------------------

    private string? folder;
    private string? pictured;
    private BundleReport shared;
    private byte[]? bundle;
    private LoadedBundle unpacked;

    [AfterScenario]
    public void RemoveTheFolder()
    {
        if (folder is not null) Directory.Delete(folder, recursive: true);
    }

    [Given("a picture module pointed at a private key on this machine")]
    public void GivenAPictureThatIsAKey()
    {
        folder = Directory.CreateTempSubdirectory("flyback-specs").FullName;
        pictured = Path.Combine(folder, "id_rsa");
        File.WriteAllText(pictured, "-----BEGIN OPENSSH PRIVATE KEY-----");

        PictureExtra.Set(context.Add("shown", NodeCatalog.PictureTypeId), pictured);
    }

    [Given("a picture module pointed at a picture on another machine")]
    public void GivenAPictureElsewhere()
    {
        pictured = @"\\somebody\share\moon.png";

        PictureExtra.Set(context.Add("shown", NodeCatalog.PictureTypeId), pictured);
    }

    [When("the patch is saved as a bundle")]
    public void WhenSavedAsABundle()
    {
        shared = PatchBundle.Write(new MemoryStream(), context.Patch, path => PatchPaths.Carriable(path, folder));
    }

    [Then("the bundle carries nothing, and says the key could not be read")]
    public void ThenTheKeyStaysBehind()
    {
        shared.Carried.ShouldBeEmpty();
        shared.Missing.ShouldBe([pictured!]);
    }

    [Given("a bundle holding a file named to climb out of its folder")]
    public void GivenABundleThatClimbsOut()
    {
        PictureExtra.Set(context.Add("shown", NodeCatalog.PictureTypeId), "moon.png");

        using var packed = new MemoryStream();
        PatchBundle.Write(packed, context.Patch, _ => [1, 2, 3]);

        using (var zip = new ZipArchive(packed, ZipArchiveMode.Update, leaveOpen: true))
        using (var writing = zip.CreateEntry(PatchBundle.FilesFolder + "../../Startup/run.bat").Open())
            writing.Write([1, 2, 3]);

        bundle = packed.ToArray();
    }

    [When("the bundle is opened")]
    public void WhenTheBundleIsOpened()
    {
        unpacked = PatchBundle.Read(new MemoryStream(bundle.ShouldNotBeNull()));
    }

    [Then("it carries only the files it packed")]
    public void ThenOnlyItsOwnFiles() =>
        unpacked.Files.Keys.ShouldBe([PatchBundle.FilesFolder + "moon.png"]);

    [Then("the picture is not looked for, because it is on another machine")]
    public void ThenNotLookedFor()
    {
        var pictures = new ImageLibrary();

        pictures.Find(pictured!).ShouldBeNull();
        pictures.Explain(pictured!).ShouldContain("another machine");
    }

    // --- the library folder ---------------------------------------------------

    private string? library;
    private string? patchFolder;
    private string? savedShowing;
    private Opened openedWithLibrary;
    private NodeInstance? chosenFor;

    [AfterScenario]
    public void RemoveTheLibrary()
    {
        if (library is not null) Directory.Delete(library, recursive: true);
        if (patchFolder is not null) Directory.Delete(patchFolder, recursive: true);
    }

    [Given("a picture {string} in the library folder")]
    public void GivenAPictureInTheLibrary(string name)
    {
        library = Directory.CreateTempSubdirectory("flyback-library").FullName;

        WritePicture(Path.Combine(library, name), width: 2);
    }

    [Given("a saved patch showing {string}, with no such picture beside it")]
    public void GivenAPatchWithoutThePicture(string name) => SaveShowing(name);

    [Given("a saved patch showing {string}, with a picture of that name beside it")]
    public void GivenAPatchWithThePicture(string name)
    {
        SaveShowing(name);
        WritePicture(Path.Combine(patchFolder!, name), width: 1);
    }

    [When("the patch is opened with the library folder")]
    public void WhenOpenedWithTheLibrary() =>
        openedWithLibrary = PatchFile.Open(new FileInfo(savedShowing.ShouldNotBeNull()), library).Patch.ShouldNotBeNull();

    [When("that picture is chosen for a picture module")]
    public void WhenChosenFromTheLibrary()
    {
        var picked = Directory.GetFiles(library.ShouldNotBeNull(), "*.png", SearchOption.AllDirectories).Single();

        chosenFor = context.Add("shown", NodeCatalog.PictureTypeId);
        PictureExtra.Set(chosenFor, PatchPaths.Named(picked, library));
    }

    [Then("it compiles with nothing wrong")]
    public void ThenItCompilesClean() =>
        openedWithLibrary.Patch
            .CompileForVideo(samples: openedWithLibrary.Samples, pictures: openedWithLibrary.Pictures)
            .Issues.ShouldBeEmpty();

    [Then("the picture shown is the one beside the patch")]
    public void ThenTheOneBeside()
    {
        var shown = PictureExtra.Of(openedWithLibrary.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId));

        openedWithLibrary.Pictures.Find(shown.ShouldNotBeNull()).ShouldNotBeNull().Width.ShouldBe(1);
    }

    [Then("the patch names it {string}")]
    public void ThenNamed(string name) => PictureExtra.Of(chosenFor.ShouldNotBeNull()).ShouldBe(name);

    /// <summary>A picture module on the screen, saved in a folder of its own.</summary>
    private void SaveShowing(string name)
    {
        PictureExtra.Set(context.Add("shown", NodeCatalog.PictureTypeId), name);
        context.Add("screen", "output");
        context.Wire("shown", 0, "screen", "color");

        patchFolder = Directory.CreateTempSubdirectory("flyback-patch").FullName;
        savedShowing = Path.Combine(patchFolder, "shown.fbk");
        File.WriteAllText(savedShowing, PatchIO.ToJson(context.Patch));
    }

    /// <summary>A black picture one pixel tall, its width telling it apart from another of the same name.</summary>
    private static void WritePicture(string path, int width)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var bgra = new byte[width * 4];

        for (var x = 0; x < width; x++) bgra[x * 4 + 3] = 255;

        PngWriter.WriteBgra(path, bgra, width, 1, width * 4);
    }

    private void Rainbow()
    {
        context.Add("coords", "coord");
        context.Add("tint", "color.hsv");
        context.Add("screen", "output");
        context.Wire("coords", "x", "tint", "hue");
        context.Wire("tint", "color", "screen", "color");
    }

    private void FilteredSine(float frequency)
    {
        context.Add("tone", "osc.sine");
        context.SetInput("tone", "freq", frequency);
        context.Add("filter", NodeCatalog.FilterTypeId);
        context.Wire("tone", "out", "filter", "in");
        context.Add("screen", "output");
        context.Wire("filter", "low", "screen", "left");
        context.SetInput("screen", "volume", 1f);
    }

    /// <summary>The audio program for the patch as it now stands, evaluated a fixed stretch, from fresh memory.</summary>
    private double[] Played()
    {
        var program = context.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var state = new DelayState(program, PatchContext.SampleRate);
        var registers = program.AllocateRegisters();
        var heard = new double[200];

        for (var i = 0; i < heard.Length; i++)
        {
            program.Evaluate(0d, 0d, i / (double)PatchContext.SampleRate, registers, default, state);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }
}
