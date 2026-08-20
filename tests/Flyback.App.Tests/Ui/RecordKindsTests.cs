using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Which kinds of file the record dialog offers. The same guard the export
/// dialog has, asked about a take: nothing standing between a person and a
/// recording of nothing but this list.
/// </summary>
public class RecordKindsTests
{
    private static Patch Wired(bool picture, bool sound)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var source = builder.Add("value", 0, 0);
        var output = builder.Add(NodeCatalog.OutputTypeId, 400, 0);

        if (picture) builder.Wire(source, 0, output, NodeCatalog.OutputColorPort);
        if (sound) builder.Wire(source, 0, output, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    private static string[] Names(Patch patch) =>
        [.. MainWindow.RecordKinds(patch).Select(k => k.Name)];

    /// <summary>Video first, for the reason the export gives: an AVI carries the sound too.</summary>
    [Fact]
    public void A_patch_with_both_is_offered_video_and_audio() =>
        Names(Wired(picture: true, sound: true)).ShouldBe(["AVI video", "WAV audio"]);

    /// <summary>
    /// A silent AVI is a complete recording of a patch that makes no sound, so it
    /// is offered — unlike a WAV of the same patch, which could only be silence.
    /// </summary>
    [Fact]
    public void A_patch_with_no_sound_is_offered_a_silent_video() =>
        Names(Wired(picture: true, sound: false)).ShouldBe(["AVI video"]);

    /// <summary>Nothing reaches the screen, so an AVI could only be a black rectangle.</summary>
    [Fact]
    public void A_patch_with_no_picture_is_offered_only_audio() =>
        Names(Wired(picture: false, sound: true)).ShouldBe(["WAV audio"]);

    /// <summary>A still is not a recording, whatever the patch draws.</summary>
    [Fact]
    public void A_still_is_never_offered()
    {
        Names(Wired(picture: true, sound: true)).ShouldNotContain("PNG image");
        Names(Wired(picture: true, sound: false)).ShouldNotContain("PNG image");
    }

    /// <summary>
    /// An Output nothing reaches. Empty rather than a default, so the button says
    /// there is nothing to record instead of opening a dialog that can only
    /// produce a file of nothing.
    /// </summary>
    [Fact]
    public void A_patch_that_reaches_nothing_is_offered_nothing() =>
        MainWindow.RecordKinds(Wired(picture: false, sound: false)).ShouldBeEmpty();

    /// <summary>
    /// Whatever can be exported can be recorded, save for the still. A patch that
    /// the export offers something for and the recorder offers nothing for would
    /// be a hole in the feature rather than a decision.
    /// </summary>
    [Fact]
    public void Every_preset_that_can_be_exported_can_be_recorded()
    {
        foreach (var preset in Presets.All)
        {
            var patch = preset.Build(NodeCatalog.BuiltIn);

            if (preset.Name == "Empty") continue;

            MainWindow.RecordKinds(patch)
                .ShouldNotBeEmpty($"the '{preset.Name}' preset should have something to record");
        }
    }
}
