using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

/// <summary>An Expression's formula: one line of text, read where the module is compiled.</summary>
/// <remarks>
/// Declared as a <see cref="ExtraField.Text"/> field, so the panel, the text
/// language and the assistant write it the way they write any plugin's field
/// (ADR-0055). What is its own is reading it: a formula that does not read is a
/// complaint here and nought out of the module, the bargain a missing file has
/// with a Sample.
/// </remarks>
public sealed record FormulaExtra(IReadOnlyDictionary<string, NodeDef> Functions) : NodeExtra
{
    /// <summary>What this is filed under, in a saved patch and on a context.</summary>
    public const string StateKey = "expression";

    /// <summary>The one field: the formula as typed.</summary>
    public const string FormulaField = "formula";

    /// <summary>What a fresh one carries: the Multiply and Add a formula most often replaces.</summary>
    public const string Fresh = "a * b + c";

    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields { get; } = [FieldOf];

    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var text = Of(node);

        if (Formula.Read(text, Functions, out var problem) is { } formula) return ctx.With(Key, formula);

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' does not read as a formula: {problem}. It gives 0 until it does."));

        return ctx;
    }

    /// <summary>The formula an instance carries, as typed.</summary>
    public static string Of(NodeInstance node) =>
        ((ExtraField.Text)FieldOf).Value(node.StateOf(StateKey)?[FormulaField]);

    public override string Announce() =>
        $"  {StateKey} {FormulaField}, the formula as a string — not a knob";

    private static readonly ExtraField FieldOf = new ExtraField.Text(FormulaField, "formula", Fresh)
    {
        Help = "The formula over 'a' to 'd', as typed. One that does not read gives 0.",
    };
}