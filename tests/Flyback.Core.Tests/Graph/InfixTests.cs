using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The brackets a sum needs as the language reads it, and no more: the one rule
/// the binder, the printer and the fusing spell a formula by.
/// </summary>
public class InfixTests
{
    private static readonly Infix.Leaf A = new("a"), B = new("b"), C = new("c");

    [Fact]
    public void A_sum_inside_a_product_is_bracketed_on_either_side()
    {
        Infix.Write(new Infix.Operation('*', new Infix.Operation('+', A, B), C)).ShouldBe("(a + b) * c");
        Infix.Write(new Infix.Operation('*', A, new Infix.Operation('+', B, C))).ShouldBe("a * (b + c)");
    }

    [Fact]
    public void A_product_inside_a_sum_needs_no_brackets() =>
        Infix.Write(new Infix.Operation('+', new Infix.Operation('*', A, B), C)).ShouldBe("a * b + c");

    /// <summary>Floats do not reassociate, so what the tree groups on the right stays grouped.</summary>
    [Fact]
    public void The_right_of_an_operator_as_strong_as_itself_keeps_its_brackets()
    {
        Infix.Write(new Infix.Operation('+', A, new Infix.Operation('+', B, C))).ShouldBe("a + (b + c)");
        Infix.Write(new Infix.Operation('+', new Infix.Operation('+', A, B), C)).ShouldBe("a + b + c");
        Infix.Write(new Infix.Operation('/', A, new Infix.Operation('*', B, C))).ShouldBe("a / (b * c)");
    }

    [Fact]
    public void A_negation_brackets_a_sum_and_a_product_but_not_a_value()
    {
        Infix.Write(new Infix.Negation(new Infix.Operation('+', A, B))).ShouldBe("-(a + b)");
        Infix.Write(new Infix.Negation(new Infix.Operation('*', A, B))).ShouldBe("-(a * b)");
        Infix.Write(new Infix.Negation(A)).ShouldBe("-a");
        Infix.Write(new Infix.Negation(new Infix.Call("floor", [A]))).ShouldBe("-floor(a)");
    }

    /// <summary>The reader takes a minus before a number as part of it, so none needs brackets.</summary>
    [Fact]
    public void A_negative_number_is_written_as_it_is()
    {
        Infix.Write(new Infix.Operation('-', A, new Infix.Number(-1))).ShouldBe("a - -1");
        Infix.Write(new Infix.Operation('*', new Infix.Number(-1), A)).ShouldBe("-1 * a");
        Infix.Write(new Infix.Operation('+', A, new Infix.Number(1))).ShouldBe("a + 1");
    }

    [Fact]
    public void A_number_is_written_as_given_or_as_the_shortest_spelling_that_reads_back()
    {
        Infix.Write(new Infix.Number(0.1f)).ShouldBe("0.1");
        Infix.Write(new Infix.Number(0.1f, "0.10")).ShouldBe("0.10");
    }

    [Fact]
    public void A_call_lists_its_arguments_as_they_are() =>
        Infix.Write(new Infix.Call("clamp", [new Infix.Operation('+', A, B), new Infix.Number(0), new Infix.Number(1)]))
            .ShouldBe("clamp(a + b, 0, 1)");
}
