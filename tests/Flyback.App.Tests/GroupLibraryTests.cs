using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>
/// The groups somebody kept: a folder of patch files, and nothing else.
/// </summary>
/// <remarks>
/// Nothing here touches a palette or a window. What is worth pinning is that a
/// kept group is an ordinary patch fragment on the disk — so it survives the
/// round trip with its box, its name and its wires, and so removing one is
/// removing a file and not editing an index.
/// </remarks>
public class GroupLibraryTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-groups-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private GroupLibrary Library() => new(NodeCatalog.BuiltIn, folder);

    /// <summary>Time into a sine into the Output, with the middle pair boxed up.</summary>
    private static Patch Chain(out NodeGroup group, string? named = "Voice")
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 0, 0);
        var osc = b.Add("osc.sine", 300, 0, (1, 220f));
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0);

        b.Wire(time, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        group = b.Patch.Group([time.Id, osc.Id]) ?? throw new InvalidOperationException("no group");
        group.Rename(named);

        return b.Patch;
    }

    [Fact]
    public void A_kept_group_is_listed_by_its_name()
    {
        var patch = Chain(out var group);
        var library = Library();

        library.All.ShouldBeEmpty("nothing has been kept yet");

        library.Save(group, patch);

        var kept = library.All.ShouldHaveSingleItem();

        kept.Name.ShouldBe("Voice");
        kept.Modules.ShouldBe(2);
        kept.IsComplete.ShouldBeTrue();
    }

    /// <summary>
    /// The whole point of keeping one: what comes back is the box, not two
    /// modules that were once in a box.
    /// </summary>
    [Fact]
    public void What_is_kept_is_the_box_and_what_is_in_it()
    {
        var patch = Chain(out var group);

        Library().Save(group, patch);

        var kept = Library().All.ShouldHaveSingleItem();
        var box = kept.Fragment.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        box.Name.ShouldBe("Voice");
        box.Members.Count.ShouldBe(2);

        // Reading a patch adds an Output to anything short of one, so the
        // fragment has one on the way back in and pasting drops it again — the
        // same round trip the clipboard makes. What was kept is the other two.
        kept.Fragment.Nodes
            .Where(n => !NodeCatalog.IsSink(n.TypeId))
            .Select(n => n.TypeId)
            .Order()
            .ShouldBe(["osc.sine", "time"]);

        kept.Fragment.Connections.Count.ShouldBe(1, "the wire between them came too");

        // And the knobs, which is what makes a kept group worth keeping rather
        // than rebuilding: it comes back set the way it was left.
        kept.Fragment.Nodes.Single(n => n.TypeId == "osc.sine").InputValues[1].ShouldBe(220f);
    }

    /// <summary>
    /// A folder of patch files and no index, so what is on the disk is the whole
    /// truth about what is kept — a second library reads exactly what the first
    /// one wrote.
    /// </summary>
    [Fact]
    public void What_one_library_keeps_another_one_finds()
    {
        var patch = Chain(out var group);

        Library().Save(group, patch);

        Library().All.Select(entry => entry.Name).ShouldBe(["Voice"]);
    }

    [Fact]
    public void Keeping_one_under_a_name_already_kept_replaces_it()
    {
        var library = Library();

        var one = Chain(out var first);
        library.Save(first, one);
        var two = Chain(out var second);
        library.Save(second, two);

        library.All.ShouldHaveSingleItem().Name.ShouldBe("Voice");
        Directory.GetFiles(folder).Length.ShouldBe(1, "the same name is the same file");
    }

    [Fact]
    public void Two_names_are_two_entries()
    {
        var library = Library();

        var voiced = Chain(out var voice);
        library.Save(voice, voiced);

        var patch = Chain(out var drums, "Drum bus");
        library.Save(drums, patch);

        library.All.Select(entry => entry.Name).ShouldBe(["Drum bus", "Voice"], "by name");
    }

    /// <summary>
    /// The palette lists a group by its name, so a group without one cannot be
    /// kept — there would be nothing to call it.
    /// </summary>
    [Fact]
    public void A_group_with_no_name_cannot_be_kept()
    {
        var patch = Chain(out var group, named: null);

        Should.Throw<InvalidOperationException>(() => Library().Save(group, patch));
        Library().All.ShouldBeEmpty();
    }

    /// <summary>
    /// A name is a title and a file name is not. What goes on the disk is
    /// whatever survives the sieve; what is shown is read back out of the patch,
    /// so the two need not agree.
    /// </summary>
    [Fact]
    public void A_name_a_file_cannot_be_called_is_still_shown_as_it_was_typed()
    {
        var patch = Chain(out var group, "In/Out: 2");

        Library().Save(group, patch);

        Library().All.ShouldHaveSingleItem().Name.ShouldBe("In/Out: 2");
    }

    [Fact]
    public void Removing_one_takes_its_file_with_it()
    {
        var library = Library();

        var patch = Chain(out var group);
        library.Save(group, patch);

        library.Remove(library.All.Single()).ShouldBeTrue();

        library.All.ShouldBeEmpty();
        Directory.GetFiles(folder).ShouldBeEmpty();
        Library().All.ShouldBeEmpty("and it stays gone");
    }

    /// <summary>
    /// A patch file dropped into the folder by hand is on the list, under the
    /// file's own name. Which follows from a kept group being an ordinary patch
    /// rather than a format of this feature's own — and is worth having.
    /// </summary>
    [Fact]
    public void A_patch_dropped_into_the_folder_is_listed_too()
    {
        Directory.CreateDirectory(folder);

        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add("time", 0, 0);

        File.WriteAllText(
            Path.Combine(folder, $"Handmade.{PatchIO.FileExtension}"),
            PatchIO.ToJson(b.Patch, NodeCatalog.BuiltIn));

        Library().All.ShouldHaveSingleItem().Name.ShouldBe("Handmade");
    }

    /// <summary>
    /// Never throws on the way in. A folder with rubbish in it is a shorter list
    /// and not a failure to start — the file is skipped and the rest are read.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_a_patch_is_skipped()
    {
        var patch = Chain(out var group);
        Library().Save(group, patch);

        File.WriteAllText(Path.Combine(folder, $"broken.{PatchIO.FileExtension}"), "{ this is not");

        Library().All.Select(entry => entry.Name).ShouldBe(["Voice"]);
    }

    [Fact]
    public void A_folder_that_is_not_there_is_an_empty_list() =>
        new GroupLibrary(NodeCatalog.BuiltIn, Path.Combine(folder, "nowhere")).All.ShouldBeEmpty();

    /// <summary>
    /// Kept shut, whether or not it was shut when it was kept. A box is what a
    /// kept group is for, and one that arrived open would be the same modules
    /// with a dashed line round them.
    /// </summary>
    [Fact]
    public void A_group_kept_while_open_is_kept_shut()
    {
        var patch = Chain(out var group);

        group.Collapsed = false;

        Library().Save(group, patch);

        Library().All
            .ShouldHaveSingleItem()
            .Fragment.Groups.ShouldNotBeNull()
            .ShouldHaveSingleItem()
            .Collapsed.ShouldBeTrue();

        // And the one on the canvas is left as it was being worked on: what was
        // kept is a copy, and shutting it here would be reaching back through a
        // save to rearrange somebody's screen.
        group.Collapsed.ShouldBeFalse();
    }
}
