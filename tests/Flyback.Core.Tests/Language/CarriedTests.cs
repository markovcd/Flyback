using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// What a module carries that is not a knob, said in the language and written
/// back out of it.
/// </summary>
/// <remarks>
/// Four kinds, and the language has to say all four or a patch cannot be
/// written as text without losing part of itself. The engine's own three — a
/// tune, a scale and a file — have had a spelling since the language shipped. A
/// plugin's declared fields are the fourth, and they are named arguments like
/// any knob (ADR-0055), addressed by key rather than by label because a label is
/// free to be reworded.
/// </remarks>
public class CarriedTests
{
    private static readonly ModuleProvider Provider = new("test.carried", "Carried modules");

    /// <summary>A plugin's kind, written the way a plugin would write one.</summary>
    private sealed record DialsExtra : NodeExtra
    {
        public override string Key => "dials";

        public override IReadOnlyList<ExtraField> Fields =>
        [
            // Key and label deliberately different, since the file may say
            // either and a caller only ever has the key.
            new ExtraField.Number("spread", "width", new PortSpec("spread", PortKind.Scalar, 0.5f, 0f, 2f)),
            new ExtraField.Number("root", "root", new PortSpec("root", PortKind.Scalar, 57f, 0f, 127f, -1, PortDisplay.Note)),
            new ExtraField.Toggle("wide", "wide", On: true),
            new ExtraField.Choice("shape", "shape", [new ChoiceOption("round", "Round")], "square"),
        ];
    }

    private static NodeDef Dials() => new(
        "test.carried.dials", "Dials", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, node) => [node[0]])
    {
        Extras = [new DialsExtra()],
    };

    private static ModuleCatalog Catalog() => NodeCatalog.BuiltIn.With(Provider, [Dials()]).Catalog;

    private static JsonNode? Held(NodeInstance node, string key) => node.StateOf("dials")?[key];

    /// <summary>A patch with one of them in it, its fields moved off their defaults.</summary>
    private static Patch Built(ModuleCatalog modules)
    {
        var patch = new Patch();

        patch.EnsureOutput(modules);

        var node = NodeInstance.Create(Dials(), 0, 0);

        node.SetState("dials", new JsonObject
        {
            ["spread"] = 1.25f,
            ["root"] = 60f,
            ["wide"] = false,
            ["shape"] = "round",
        });

        patch.Nodes.Add(node);
        patch.Connect(node.Id, 0, patch.Output.Id, NodeCatalog.OutputLeftPort);

        return patch;
    }

    /// <summary>
    /// A plugin's field is part of the instrument — the Fractal's octave count
    /// decides how many the program builds — so a printing that dropped it would
    /// be a printing of a different patch.
    /// </summary>
    [Fact]
    public void A_plugins_fields_survive_a_printing()
    {
        var modules = Catalog();
        var source = PatchPrinter.Print(Built(modules), modules);
        var load = PatchLanguage.Build(source, modules);

        load.Issues.ShouldBeEmpty($"{load.Report}{Environment.NewLine}{source}");

        var again = load.Patch.Nodes.Single(n => n.TypeId == "test.carried.dials");

        Held(again, "spread")!.GetValue<float>().ShouldBe(1.25f);
        Held(again, "root")!.GetValue<float>().ShouldBe(60f);
        Held(again, "wide")!.GetValue<bool>().ShouldBeFalse();
        Held(again, "shape")!.GetValue<string>().ShouldBe("round");
    }

    /// <summary>
    /// A field still holding what a fresh instance holds is written nowhere, the
    /// same as a knob at its default. A printing is for reading, and every
    /// module restating its whole shape would bury what somebody changed.
    /// </summary>
    [Fact]
    public void A_field_at_its_default_is_not_written()
    {
        var modules = Catalog();
        var patch = new Patch();

        patch.EnsureOutput(modules);

        var node = NodeInstance.Create(Dials(), 0, 0);

        patch.Nodes.Add(node);
        patch.Connect(node.Id, 0, patch.Output.Id, NodeCatalog.OutputLeftPort);

        var source = PatchPrinter.Print(patch, modules);

        source.ShouldNotContain("spread");
        source.ShouldNotContain("wide");
        source.ShouldNotContain("shape");
    }

    /// <summary>
    /// A note-scaled field is written as the note it means, the same as a knob
    /// on the same scale, because the number is not what it stands for.
    /// </summary>
    [Fact]
    public void A_field_is_written_on_the_scale_it_reads_on()
    {
        var modules = Catalog();

        PatchPrinter.Print(Built(modules), modules).ShouldContain("root: C4");
    }

    // --- and changed where the text already says them ------------------------

    private static LanguageLoad Read(string source, ModuleCatalog modules)
    {
        var load = PatchLanguage.Build(source, modules);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static string Applied(string source, Change change) =>
        source[..change.Offset] + change.Text + source[(change.Offset + change.Length)..];

    /// <summary>The one module a source describes, whatever it is called.</summary>
    private static Guid Only(LanguageLoad load, string typeId) =>
        load.Patch.Nodes.Single(n => n.TypeId == typeId).Id;

    [Fact]
    public void A_field_is_changed_where_the_text_already_says_it()
    {
        var modules = Catalog();

        const string source = "dials(spread: 1.25, wide: 0) |> out.left";

        var load = Read(source, modules);
        var node = Only(load, "test.carried.dials");

        Applied(source, load.Map.Knob(node, "spread", "0.75")!.Value)
            .ShouldBe("dials(spread: 0.75, wide: 0) |> out.left");
    }

    /// <summary>
    /// A choice is stored as an id and written as the one string the language
    /// has, so replacing it replaces the quotes with it.
    /// </summary>
    [Fact]
    public void A_choice_is_changed_with_its_quotes()
    {
        var modules = Catalog();

        const string source = "dials(shape: \"round\") |> out.left";

        var load = Read(source, modules);
        var node = Only(load, "test.carried.dials");

        Applied(source, load.Map.Knob(node, "shape", "\"oval\"")!.Value)
            .ShouldBe("dials(shape: \"oval\") |> out.left");
    }

    /// <summary>
    /// A field the file says by its label rather than by its key is still that
    /// field, and is changed where it stands rather than said twice.
    /// </summary>
    [Fact]
    public void A_field_written_by_its_label_is_found_by_its_key()
    {
        var modules = Catalog();

        const string source = "dials(width: 1.25) |> out.left";

        var load = Read(source, modules);
        var node = Only(load, "test.carried.dials");

        Applied(source, load.Map.Knob(node, "spread", "0.75")!.Value)
            .ShouldBe("dials(width: 0.75) |> out.left");
    }

    [Fact]
    public void A_field_the_text_does_not_mention_is_added_to_the_call()
    {
        var modules = Catalog();

        const string source = "dials() |> out.left";

        var load = Read(source, modules);
        var node = Only(load, "test.carried.dials");

        Applied(source, load.Map.Knob(node, "wide", "0")!.Value)
            .ShouldBe("dials(wide: 0) |> out.left");
    }

    [Fact]
    public void A_tune_is_changed_where_the_block_stands()
    {
        const string source = "let riff = notes() [ A3 C4 ]\nriff |> out.left";

        var load = Read(source, NodeCatalog.BuiltIn);
        var node = load.Patch.Nodes.Single(n => n.Name == "riff").Id;

        Applied(source, load.Map.Carried(node, "[ E4 G4 ]")!.Value)
            .ShouldBe("let riff = notes() [ E4 G4 ]\nriff |> out.left");
    }

    /// <summary>
    /// A module carrying nothing yet has no block to replace, so one is put
    /// after its call — which is the only place the language reads one.
    /// </summary>
    [Fact]
    public void A_tune_the_text_does_not_carry_is_added_after_the_call()
    {
        const string source = "let riff = notes()\nriff |> out.left";

        var load = Read(source, NodeCatalog.BuiltIn);
        var node = load.Patch.Nodes.Single(n => n.Name == "riff").Id;

        var changed = Applied(source, load.Map.Carried(node, "[ E4 G4 ]")!.Value);

        changed.ShouldBe("let riff = notes() [ E4 G4 ]\nriff |> out.left");
        Read(changed, NodeCatalog.BuiltIn).Patch.Nodes.Count.ShouldBe(load.Patch.Nodes.Count);
    }

    /// <summary>
    /// An emptied tune is written as an empty block rather than by taking the
    /// block away, the same as a knob dragged back to its default is written
    /// rather than deleted.
    /// </summary>
    [Fact]
    public void An_emptied_tune_is_written_as_an_empty_block()
    {
        const string source = "let riff = notes() [ A3 C4 ]\nriff |> out.left";

        var load = Read(source, NodeCatalog.BuiltIn);
        var node = load.Patch.Nodes.Single(n => n.Name == "riff").Id;

        var changed = Applied(source, load.Map.Carried(node, "[ ]")!.Value);
        var again = Read(changed, NodeCatalog.BuiltIn);

        StepsExtra.Of(again.Patch.Nodes.Single(n => n.Name == "riff")).ShouldBeEmpty();
    }

    [Fact]
    public void A_file_is_changed_where_the_text_names_it()
    {
        const string source = "sample(\"kick.wav\") |> out.left";

        var load = Read(source, NodeCatalog.BuiltIn);
        var node = Only(load, "audio.sample");

        Applied(source, load.Map.File(node, "snare.wav")!.Value)
            .ShouldBe("sample(\"snare.wav\") |> out.left");
    }

    /// <summary>
    /// A module naming no file yet takes one as its first argument, which is
    /// where a printing puts it and where the binder looks for it.
    /// </summary>
    [Fact]
    public void A_file_the_text_does_not_name_goes_in_first()
    {
        const string source = "sample(level: 0.5) |> out.left";

        var load = Read(source, NodeCatalog.BuiltIn);
        var node = Only(load, "audio.sample");

        var changed = Applied(source, load.Map.File(node, "snare.wav")!.Value);

        changed.ShouldBe("sample(\"snare.wav\", level: 0.5) |> out.left");
        SampleExtra.Of(Read(changed, NodeCatalog.BuiltIn).Patch.Nodes
            .Single(n => n.TypeId == "audio.sample")).ShouldBe("snare.wav");
    }

    /// <summary>
    /// A choice is a string too, so the file has to be told from it — and what
    /// tells them apart is that the file is the one without a name.
    /// </summary>
    [Fact]
    public void A_named_string_is_not_mistaken_for_the_file()
    {
        var modules = Catalog();

        const string source = "sample(shape: \"round\") |> out.left";

        var load = PatchLanguage.Build(source, modules);
        var node = load.Patch.Nodes.Single(n => n.TypeId == "audio.sample").Id;

        // The Sample has no such field, so the text is wrong — but where a file
        // would go is a question about shape rather than about meaning.
        Applied(source, load.Map.File(node, "kick.wav")!.Value)
            .ShouldStartWith("sample(\"kick.wav\", shape: \"round\")");
    }
}
