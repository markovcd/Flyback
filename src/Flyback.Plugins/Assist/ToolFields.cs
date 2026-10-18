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
    public static readonly ToolField TypeId = new("type_id", Text, Required: true, "The module's type id, e.g. osc.sine.");

    // write_patch

    public static readonly ToolField Source = new("source", Text, Required: true,
        "The whole patch in the language. Nothing is adopted unless all of it reads.");

    // edit_patch

    public static readonly ToolField Find = new("find", Text, Required: true,
        "A piece of the patch's text, exactly as it stands, found exactly once.");

    public static readonly ToolField Replace = new("replace", Text, Required: true,
        "What goes in its place. Empty deletes it.");

    public static readonly ToolField Edits = new("edits", ListOf(ObjectOf(Find, Replace)), Required: true,
        "Applied in order, each to the text the one before left. Nothing changes unless every one is found and the result reads.");

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
