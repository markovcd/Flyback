using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Specs.Support;

namespace Flyback.Core.Specs.Steps;

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
        context.Add("stranger", "pattern.noise");
        session.Before = context.Render();
        session.File = PatchIO.ToJson(context.Patch).Replace("\"pattern.noise\"", $"\"{Stranger}\"");
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

    private void Rainbow()
    {
        context.Add("coords", "coord");
        context.Add("tint", "color.hsv");
        context.Add("screen", "output");
        context.Wire("coords", "x", "tint", "hue");
        context.Wire("tint", "color", "screen", "color");
    }
}
