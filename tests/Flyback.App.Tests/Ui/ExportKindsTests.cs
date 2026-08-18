using Flyback.App;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Which kinds of file the export dialog offers. One button writes either an
/// AVI or a WAV and the name decides which, so the only thing standing between
/// a person and a file of nothing is this list.
/// </summary>
public class ExportKindsTests
{
    private static Patch Wired(bool picture, bool sound)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var source = builder.Add("value", 0, 0);
        var output = builder.Add(NodeCatalog.OutputTypeId, 400, 0);

        if (picture) builder.Wire(source, 0, output, NodeCatalog.OutputColourPort);
        if (sound) builder.Wire(source, 0, output, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    private static string[] Names(Patch patch) =>
        [.. MainWindow.ExportKinds(patch).Select(k => k.Name)];

    [Fact]
    public void A_patch_with_both_is_offered_both()
    {
        // Video first: an AVI carries the sound too, so it is the whole of what
        // the patch does and the sensible default.
        Names(Wired(picture: true, sound: true)).ShouldBe(["AVI video", "WAV audio"]);
    }

    /// <summary>Nothing reaches the speakers, so a WAV could only be silence.</summary>
    [Fact]
    public void A_patch_with_no_sound_is_offered_only_video() =>
        Names(Wired(picture: true, sound: false)).ShouldBe(["AVI video"]);

    /// <summary>Nothing reaches the screen, so an AVI could only be a black rectangle.</summary>
    [Fact]
    public void A_patch_with_no_picture_is_offered_only_audio() =>
        Names(Wired(picture: false, sound: true)).ShouldBe(["WAV audio"]);

    /// <summary>
    /// An Output nothing reaches at all. Empty rather than a default, so the
    /// button says there is nothing to write instead of opening a dialog that
    /// can only produce a file of nothing.
    /// </summary>
    [Fact]
    public void A_patch_that_reaches_nothing_is_offered_nothing() =>
        MainWindow.ExportKinds(Wired(picture: false, sound: false)).ShouldBeEmpty();

    /// <summary>Either channel is enough — the right one alone still makes a sound.</summary>
    [Fact]
    public void The_right_channel_alone_counts_as_sound()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var source = builder.Add("value", 0, 0);
        var output = builder.Add(NodeCatalog.OutputTypeId, 400, 0);

        builder.Wire(source, 0, output, NodeCatalog.OutputRightPort);

        Names(builder.Patch).ShouldBe(["WAV audio"]);
    }

    /// <summary>
    /// A knob turned up is not a wire. The Output's gain sits at a half by
    /// default and says nothing about whether anything is playing through it.
    /// </summary>
    [Fact]
    public void A_knob_on_the_output_is_not_a_signal()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        MainWindow.ExportKinds(builder.Patch).ShouldBeEmpty();
    }

    /// <summary>Every preset that ships should be exportable as something.</summary>
    [Fact]
    public void Every_preset_can_be_written_to_a_file()
    {
        foreach (var preset in Presets.All)
        {
            var patch = preset.Build(NodeCatalog.BuiltIn);

            if (preset.Name == "Empty") continue;

            MainWindow.ExportKinds(patch)
                .ShouldNotBeEmpty($"the '{preset.Name}' preset should have something to write");
        }
    }

    /// <summary>And the empty one, honestly, should not.</summary>
    [Fact]
    public void The_empty_preset_has_nothing_to_write() =>
        MainWindow.ExportKinds(Presets.Empty(NodeCatalog.BuiltIn)).ShouldBeEmpty();
}
