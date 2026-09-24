using System.IO.Compression;
using Reqnroll;
using Shouldly;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>Saving, opening, writing out as text, undoing and pasting.</summary>
[Binding]
public sealed class EditingSteps(PatchContext context, Session session)
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

    [Then("the patch is tagged {string}")]
    public void ThenTagged(string tags) => context.Patch.Tags.ShouldBe(tags.Split(", "));

    [Then("the text has the line {string}")]
    public void ThenTheTextHasTheLine(string line) =>
        Text.Source.Split('\n').Select(l => l.TrimEnd('\r')).ShouldContain(line, Text.Source);

    [Then("it reads without complaint")]
    public void ThenReadsCleanly() => Text.Ok.ShouldBeTrue(Text.Report);

    /// <summary>The line is quoted beside its number, so the reader sees which statement is at fault.</summary>
    [Then("the complaint quotes line {int}")]
    public void ThenTheComplaintQuotes(int line)
    {
        var quoted = Text.Source.Split('\n')[line - 1].TrimEnd('\r');

        Text.Report.ShouldContain($"{line}:");
        Text.Report.ShouldContain(quoted);
    }

    [Then("it suggests {string}")]
    public void ThenItSuggests(string name) => Text.Report.ShouldContain(name);

    [When("the fixes it suggests are made")]
    public void WhenTheFixesAreMade() => Read(LanguageFix.Apply(Text.Source, Text.Issues));

    [Then("the panel has a knob {string} resting at {float}")]
    public void ThenThePanelHasAKnob(string name, float value) =>
        context.Patch.Controls.ShouldNotBeNull().ShouldContain(knob => knob.Name == name && Math.Abs(knob.Value - value) < 1e-6f);

    [Then("the patch has a shut group {string} of {int} modules")]
    public void ThenTheGroup(string name, int count) =>
        context.Patch.Groups.ShouldNotBeNull().ShouldContain(group => group.Name == name && group.Collapsed && group.Members.Count == count);

    [Then("that is the only complaint")]
    public void ThenThatIsTheOnlyComplaint() => Text.Issues.ShouldHaveSingleItem(Text.Report);

    [Then("the complaint says {string}")]
    public void ThenTheComplaintSays(string words) => Text.Report.ShouldContain(words);

    // --- undo -----------------------------------------------------------------

    [Given("the patch has just been opened")]
    public void GivenJustOpened()
    {
        session.History = new PatchHistory(NodeCatalog.BuiltIn);
        session.History.Opened(context.Patch);
    }

    [When("the level is deleted")]
    public void WhenTheLevelIsDeleted()
    {
        context.Remove("level");
        History.Record(context.Patch);
    }

    [When("that is undone")]
    public void WhenUndone() => context.Replace(History.Undo().ShouldNotBeNull("there was nothing to undo"));

    [When("it is redone")]
    public void WhenRedone() => context.Replace(History.Redo().ShouldNotBeNull("there was nothing to redo"));

    [Then("there is nothing to undo")]
    public void ThenNothingToUndo() => History.CanUndo.ShouldBeFalse();

    [Then("the patch has unsaved changes")]
    public void ThenModified() => History.IsModified.ShouldBeTrue();

    [Then("the patch has no unsaved changes")]
    public void ThenUnmodified() => History.IsModified.ShouldBeFalse();

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
    public void WhenEverythingIsPasted() => Paste([.. context.Patch.Nodes.Select(n => n.Id)]);

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

    // --- building blocks ------------------------------------------------------

    private PatchLoad Opened => session.Opened.ShouldNotBeNull("no file has been opened");

    private LanguageLoad Text => session.Text.ShouldNotBeNull("no text has been read");

    private PatchHistory History => session.History.ShouldNotBeNull("the patch was never opened");

    private NodeInstance Pasted(string typeId) => session.Pasted.Single(n => n.TypeId == typeId);

    private void Read(string source)
    {
        session.Text = PatchLanguage.Build(source, NodeCatalog.BuiltIn);
        context.Replace(session.Text.Patch);
    }

    private void Paste(Guid[] ids)
    {
        var fragment = PatchClipboard.Copy(context.Patch, ids);
        session.Pasted = PatchClipboard.Paste(context.Patch, fragment, 40, 40);
        context.Replace(context.Patch);
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
