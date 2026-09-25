using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>keyboard scale [ D dorian ]</c>: how the computer keyboard is laid out, which
/// the patch carries rather than any module in it (ADR-0099).
/// </summary>
public class KeyboardLineTests
{
    private static LanguageLoad Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static string Applied(string source, Change change) =>
        source[..change.Offset] + change.Text + source[(change.Offset + change.Length)..];

    [Fact]
    public void A_scale_line_lays_the_keyboard_out()
    {
        Built("keyboard scale [ D dorian ]\nmidi.in().pitch |> out.left")
            .Patch.Keyboard.ShouldBe(new KeyboardScale(2, "dorian"));
        Built("keyboard scale [ F# harmonic-minor ]\nmidi.in().pitch |> out.left")
            .Patch.Keyboard.ShouldBe(new KeyboardScale(6, "harmonic-minor"));
    }

    [Fact]
    public void Saying_nothing_is_a_piano()
    {
        Built("midi.in().pitch |> out.left").Patch.Keyboard.ShouldBeNull();
        Built("keyboard piano\nmidi.in().pitch |> out.left").Patch.Keyboard.ShouldBeNull();
    }

    [Fact]
    public void A_scale_it_does_not_have_is_refused_by_name()
    {
        var load = PatchLanguage.Build("keyboard scale [ D dorain ]\nmidi.in().pitch |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldContain(issue => issue.Code == IssueCode.UnknownScale && issue.Message.Contains("'dorain'"));
        load.Patch.Keyboard.ShouldBeNull();
    }

    [Theory]
    [InlineData("keyboard scale [ C D E G A ]")]
    [InlineData("keyboard scale [ dorian ]")]
    [InlineData("keyboard scale [ ]")]
    public void A_scale_that_is_not_a_tonic_and_a_scale_is_refused(string line)
    {
        PatchLanguage.Build(line + "\nmidi.in().pitch |> out.left", NodeCatalog.BuiltIn)
            .Issues.ShouldContain(issue => issue.Message.Contains("a tonic and a scale"));
    }

    /// <summary>The line only reads as one with a layout after it.</summary>
    [Fact]
    public void A_binding_called_keyboard_is_still_a_name()
    {
        var patch = Built("let keyboard = midi.in()\nkeyboard.pitch |> out.left").Patch;

        patch.Keyboard.ShouldBeNull();
        patch.Nodes.ShouldContain(n => n.TypeId == NodeCatalog.MidiTypeId);
    }

    [Fact]
    public void Saying_it_twice_is_refused()
    {
        var load = PatchLanguage.Build(
            "keyboard scale [ E phrygian ]\nkeyboard piano\nmidi.in().pitch |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldContain(issue => issue.Message.Contains("already laid out"));
        load.Patch.Keyboard.ShouldBe(new KeyboardScale(4, "phrygian"));
    }

    [Fact]
    public void A_layout_that_is_not_one_is_refused()
    {
        PatchLanguage.Build("keyboard organ\nmidi.in().pitch |> out.left", NodeCatalog.BuiltIn)
            .Issues.ShouldContain(issue => issue.Message.Contains("not a layout"));
    }

    [Fact]
    public void A_printing_says_the_layout_first_and_builds_back_to_it()
    {
        var patch = Built("midi.in().pitch |> out.left").Patch;
        patch.Keyboard = new KeyboardScale(10, "lydian-dominant");

        var source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        source.ShouldStartWith("keyboard scale [ A# lydian-dominant ]");
        Built(source).Patch.Keyboard.ShouldBe(new KeyboardScale(10, "lydian-dominant"));
    }

    [Fact]
    public void A_piano_prints_nothing()
    {
        PatchPrinter.Print(Built("midi.in().pitch |> out.left").Patch, NodeCatalog.BuiltIn)
            .ShouldNotContain("keyboard");
    }

    /// <summary>
    /// The line is not a module, so it must not take a number another line would
    /// have had — adding it would rename every module after it.
    /// </summary>
    [Fact]
    public void Adding_the_line_leaves_every_module_its_identity()
    {
        var without = Built("midi.in().pitch |> sine() |> out.left").Patch;
        var with = Built("keyboard scale [ C ionian ]\nmidi.in().pitch |> sine() |> out.left").Patch;

        with.Nodes.Select(n => n.Id).ShouldBe(without.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void The_panel_writes_a_new_line_at_the_top()
    {
        const string source = "midi.in().pitch |> out.left\n";

        var change = Built(source).Map.Keyboard("keyboard scale [ C ionian ]");

        change.ShouldNotBeNull();
        Applied(source, change.Value).ShouldBe("keyboard scale [ C ionian ]\n\nmidi.in().pitch |> out.left\n");
    }

    [Fact]
    public void The_panel_changes_the_line_where_it_stands()
    {
        const string source = "# lead\nkeyboard scale [ C ionian ]\nmidi.in().pitch |> out.left\n";

        var change = Built(source).Map.Keyboard("keyboard scale [ G mixolydian ]");

        Applied(source, change!.Value).ShouldBe("# lead\nkeyboard scale [ G mixolydian ]\nmidi.in().pitch |> out.left\n");
    }

    [Fact]
    public void Going_back_to_a_piano_takes_the_line_out()
    {
        const string source = "keyboard scale [ C ionian ]\nmidi.in().pitch |> out.left\n";

        var change = Built(source).Map.Keyboard(null);

        Applied(source, change!.Value).ShouldBe("midi.in().pitch |> out.left\n");
    }

    [Fact]
    public void A_text_that_already_says_it_is_left_alone()
    {
        Built("keyboard scale [ C ionian ]\nmidi.in().pitch |> out.left").Map.Keyboard("keyboard scale [ C ionian ]").ShouldBeNull();
        Built("midi.in().pitch |> out.left").Map.Keyboard(null).ShouldBeNull();
    }

    [Fact]
    public void A_saved_patch_keeps_the_layout()
    {
        var patch = Built("midi.in().pitch |> out.left").Patch;
        patch.Keyboard = new KeyboardScale(9, "aeolian");

        PatchIO.Read(PatchIO.ToJson(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn)
            .Patch.Keyboard.ShouldBe(new KeyboardScale(9, "aeolian"));
    }

    [Fact]
    public void A_piano_is_not_written_into_the_file()
    {
        PatchIO.ToJson(Built("midi.in().pitch |> out.left").Patch, NodeCatalog.BuiltIn)
            .ShouldNotContain($"\"{nameof(Patch.Keyboard)}\"", Case.Sensitive);
    }
}
