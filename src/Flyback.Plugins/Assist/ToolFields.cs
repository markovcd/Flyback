using Flyback.Core.Graph;
using static Flyback.Plugins.Assist.ToolKind;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Every argument a tool takes, declared once: <see cref="ToolTable"/> tells a
/// provider about it from here, and the tool's body reads it through the same field.
/// </summary>
/// <remarks>
/// Where a description depends on the limits or the senses of a run,
/// <see cref="ToolTable"/> supplies it with <see cref="ToolField.Described"/>.
/// </remarks>
internal static class ToolFields
{
    /// <summary>The module a tool acts on.</summary>
    public static readonly ToolField Handle = new("handle", Text, Required: true);

    public static readonly ToolField TypeId = new("type_id", Text, Required: true, "The module's type id, e.g. osc.sine.");

    // write_patch

    public static readonly ToolField Source = new("source", Text, Required: true,
        "The whole patch in the language. Nothing is adopted unless all of it reads.");

    // add_module and set_knobs

    public static readonly ToolField NewHandle = new("handle", Text, Description: "What to call it, e.g. sine1.");

    public static readonly ToolField KnobPort = new("port", Text, Required: true, "The input's name.");

    public static readonly ToolField KnobValue = new("value", Number, Required: true);

    public static readonly ToolField Knobs = new("knobs", ListOf(ObjectOf(KnobPort, KnobValue)), Required: true);

    public static readonly ToolField PlacedKnobs = Knobs with { Required = false, Description = "Inputs to set as it is placed." };

    // set_steps

    public static readonly ToolField StepValue = new("value", Number, Required: true);

    public static readonly ToolField StepLength = new("length", Number);

    public static readonly ToolField StepVolume = new("volume", Number);

    public static readonly ToolField Steps = new("notes", ListOf(ObjectOf(StepValue, StepLength, StepVolume)), Required: true);

    // set_scale

    public static readonly ToolField ScaleNotes = new("notes", ListOf(Whole(0, Pitch.Classes - 1)), Required: true);

    // set_arrangement

    public static readonly ToolField Parts = new("parts", ListOf(Text), Required: true);

    // set_keyboard

    public static readonly ToolField Layout = new("layout", OneOf(["piano", "scale"]), Required: true);

    public static readonly ToolField Tonic = new("tonic", Whole(0, Pitch.Classes - 1));

    public static readonly ToolField KeyScale = new("scale", OneOf(Chords.Scales.Select(scale => scale.Id)));

    // set_length

    public static readonly ToolField Length = new("seconds", Between(PatchLength.Shortest, PatchLength.Longest), Required: true);

    // set_sample and set_picture

    public static readonly ToolField SamplePath = new("path", Text, Required: true,
        "Where the WAV, MP3, MIDI, SVG, OBJ or PNG file is, absolute or beside the patch.");

    public static readonly ToolField PicturePath = new("path", Text, Required: true,
        "Where the PNG is, absolute or beside the patch.");

    // set_extra

    public static readonly ToolField Extra = new("extra", Text, Required: true, "Which extra, as the listing names it.");

    public static readonly ToolField Field = new("field", Text, Required: true, "Which of its values.");

    public static readonly ToolField FieldValue = new("value", Anything, Required: true,
        "A number, a boolean, a choice's id as a string, or text — matching the field.");

    // connect, disconnect and switch_module

    public static readonly ToolField WireFrom = new("from", Text, Required: true, "Handle of the module the signal leaves.");

    public static readonly ToolField WireFromPort = new("from_port", Text, Description: "Output name. Optional when there is only one.");

    public static readonly ToolField WireTo = new("to", Text, Required: true, "Handle of the module the signal arrives at.");

    public static readonly ToolField WireToPort = new("to_port", Text, Required: true, "Input name.");

    public static readonly ToolField InputPort = new("port", Text, Required: true, "The input's name.");

    public static readonly ToolField Off = new("off", Flag, Description: "False switches it back on. Defaults to true.");

    // propose

    public static readonly ToolField Summary = new("summary", Text, Required: true, "One line: what this patch does.");

    public static readonly ToolField StartsSilent = new("starts_silent", Flag);

    // render, listen and measure

    /// <summary>Where on the patch's timeline a look, a listen or a measurement starts.</summary>
    public static readonly ToolField From = new("from", Number);

    /// <summary>How long a listen or a measurement runs.</summary>
    public static readonly ToolField Seconds = new("seconds", Number);

    public static readonly ToolField Times = new("times", ListOf(Number));

    /// <summary>What a look or a listen is for, which the assistant writes for its own record.</summary>
    public static readonly ToolField Note = new("note", Text);

    public static readonly ToolField Handles = new("handles", ListOf(Text), Description: "Modules to measure. Left out, every module.");

    // find_modules and describe_preset

    public static readonly ToolField Query = new("query", Text, Required: true);

    public static readonly ToolField PresetName = new("name", Text, Required: true);
}
