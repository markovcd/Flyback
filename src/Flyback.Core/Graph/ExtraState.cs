using System.Text.Json.Nodes;

namespace Flyback.Core.Graph;

/// <summary>
/// One instance's worth of a declared extra, parsed and ready for an emit
/// function — what a plugin reads back out of <see cref="EmitContext.Extras"/>.
/// Typed at the point of use: a plugin knows its own schema, and the tolerance
/// for a file that means nothing has already been applied.
/// </summary>
public sealed class ExtraState(IReadOnlyList<ExtraField> fields, JsonNode? stored)
{
    /// <summary>What a number field holds, or its default where nothing sensible does.</summary>
    public float Number(string key) =>
        Field(key) is ExtraField.Number field ? field.Value(stored?[key]) : 0f;

    /// <summary>
    /// What a toggle field holds, or its default where nothing sensible does.
    /// Shipped plugin contract, the counterpart of <see cref="Number"/>; no built-in module declares a toggle, so only tests call it.
    /// </summary>
    internal bool Toggle(string key) =>
        Field(key) is ExtraField.Toggle field && field.Value(stored?[key]);

    /// <summary>
    /// Which option a choice field holds, or its fallback where nothing sensible
    /// does — and the empty string for a key that is not a choice at all.
    /// </summary>
    public string Chosen(string key) =>
        Field(key) is ExtraField.Choice field ? field.Value(stored?[key]) : string.Empty;

    /// <summary>
    /// What a text field holds, or its fallback where nothing sensible does — and
    /// the empty string for a key that is not text at all.
    /// </summary>
    public string Text(string key) =>
        Field(key) is ExtraField.Text field ? field.Value(stored?[key]) : string.Empty;

    private ExtraField? Field(string key) => fields.FirstOrDefault(f => f.Key == key);
}