using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Which kinds of file a patch may be saved as, and how a name decides which
/// one was meant.
/// </summary>
/// <remarks>
/// One dialog writes three things and the extension is the whole of the choice,
/// so this is the branch that decides whether a save keeps everything, keeps
/// everything plus the files it names, or keeps the instrument and not the
/// drawing of it.
/// </remarks>
public class SaveKindsTests
{
    private static string[] Names(IEnumerable<Avalonia.Platform.Storage.FilePickerFileType> kinds) =>
        [.. kinds.Select(k => k.Name)];

    /// <summary>
    /// The text is offered last, and deliberately. The two above it keep
    /// everything the editor knows; this one does not, so it should not be where
    /// the habit of pressing Save lands.
    /// </summary>
    [Fact]
    public void A_patch_is_offered_its_own_kind_first_and_the_text_last() =>
        Names(MainWindow.SaveKinds(bundled: false)).ShouldBe(["Flyback patch", "Flyback bundle", "Flyback text"]);

    /// <summary>A bundle saved again stays one without anybody typing the extension.</summary>
    [Fact]
    public void A_bundle_is_offered_its_own_kind_first() =>
        Names(MainWindow.SaveKinds(bundled: true)).ShouldBe(["Flyback bundle", "Flyback patch", "Flyback text"]);

    [Fact]
    public void Everything_that_can_be_saved_can_be_opened() =>
        Names(MainWindow.OpenKinds()).ShouldBe(Names(MainWindow.SaveKinds(bundled: false)));

    // --- what a name means -----------------------------------------------------

    [Theory]
    [InlineData("nebula.fbkb", true)]
    [InlineData("NEBULA.FBKB", true)]
    [InlineData("nebula.fbk", false)]
    [InlineData("nebula.fbks", false)]
    public void A_bundle_is_known_by_its_extension(string name, bool bundle) =>
        MainWindow.Bundled(name).ShouldBe(bundle);

    /// <summary>
    /// The two text-ish extensions differ by one character, and getting this
    /// wrong would write JSON into a file somebody expected to read.
    /// </summary>
    [Theory]
    [InlineData("nebula.fbks", true)]
    [InlineData("NEBULA.FBKS", true)]
    [InlineData("nebula.fbk", false)]
    [InlineData("nebula.fbkb", false)]
    public void The_text_is_known_by_its_extension(string name, bool text) =>
        MainWindow.Sourced(name).ShouldBe(text);

    [Fact]
    public void No_name_is_two_kinds_at_once()
    {
        foreach (var name in new[] { "a.fbk", "a.fbkb", "a.fbks" })
            (MainWindow.Bundled(name) && MainWindow.Sourced(name)).ShouldBeFalse(name);
    }

    // --- what is written -------------------------------------------------------

    /// <summary>
    /// What the text save writes, checked here rather than through the dialog:
    /// the patch goes out in the language and comes back as the same instrument.
    /// </summary>
    [Fact]
    public void The_text_a_patch_is_saved_as_builds_back_to_the_same_patch()
    {
        var patch = Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

        var written = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);
        var read = PatchLanguage.Build(written, NodeCatalog.BuiltIn);

        read.Issues.ShouldBeEmpty(read.Report);

        read.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops
            .Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K))
            .ShouldBe(patch.CompileForVideo(NodeCatalog.BuiltIn).Program.Ops
                .Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K)));
    }
}
