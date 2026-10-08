using System.Globalization;

namespace Flyback.Core.Graph;

/// <summary>
/// A sum spelled as the text language's arithmetic, with the brackets its tree
/// needs and no more: the one rule the binder, the printer and the fusing follow
/// (ADR-0106, ADR-0107).
/// </summary>
internal static class Infix
{
    /// <summary>A part of the sum: a number, a leaf already spelled, a negation, an operation or a call.</summary>
    public abstract record Part;

    /// <param name="Text">How the number is written, or null for the shortest spelling that reads back as the same float.</param>
    public sealed record Number(float Value, string? Text = null) : Part;

    /// <summary>A socket or a signal, as the caller spells it.</summary>
    public sealed record Leaf(string Text) : Part;

    public sealed record Negation(Part Operand) : Part;

    public sealed record Operation(char Sign, Part Left, Part Right) : Part;

    public sealed record Call(string Name, IReadOnlyList<Part> Arguments) : Part;

    public static string Write(Part part) => part switch
    {
        Number number => number.Text ?? number.Value.ToString("R", CultureInfo.InvariantCulture),
        Leaf leaf => leaf.Text,
        Negation negation => Strength(negation.Operand) < Strength(negation) ? $"-({Write(negation.Operand)})" : $"-{Write(negation.Operand)}",
        Operation operation => Operated(operation),
        Call call => $"{call.Name}({string.Join(", ", call.Arguments.Select(Write))})",
        _ => throw new InvalidOperationException($"No spelling for {part.GetType().Name}."),
    };

    /// <summary>Floats do not reassociate, so the right of an operator as strong as itself keeps its brackets.</summary>
    private static string Operated(Operation operation)
    {
        var strength = Strength(operation);
        var left = Write(operation.Left);
        var right = Write(operation.Right);

        if (Strength(operation.Left) < strength) left = $"({left})";
        if (Strength(operation.Right) <= strength) right = $"({right})";

        return $"{left} {operation.Sign} {right}";
    }

    /// <summary>
    /// How tightly a part holds together as the language reads it: a sum least, a
    /// value or a call most. A negative number needs no brackets: the reader takes
    /// a minus before a number as part of it, however many minuses there are.
    /// </summary>
    private static int Strength(Part part) => part switch
    {
        Negation => 3,
        Operation { Sign: '+' or '-' } => 1,
        Operation => 2,
        _ => 4,
    };
}
