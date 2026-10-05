using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary><see cref="ExtraField.Number"/> — a number read from whatever JSON type holds it.</summary>
public class NumberFieldTests
{
    private static ExtraField.Number Voices() =>
        new("voices", "voices", new PortSpec("voices", PortKind.Scalar, 1f, 0f, 8f));

    [Fact]
    public void A_number_set_in_code_reads_back_whatever_type_it_was_made_as()
    {
        var voices = Voices();

        voices.Value(JsonValue.Create(3)).ShouldBe(3f);
        voices.Value(JsonValue.Create(3L)).ShouldBe(3f);
        voices.Value(JsonValue.Create(2.5)).ShouldBe(2.5f);
        voices.Value(JsonValue.Create(2.5f)).ShouldBe(2.5f);
        voices.Value(JsonValue.Create(4m)).ShouldBe(4f);
    }

    [Fact]
    public void A_number_read_from_text_reads_back()
    {
        Voices().Value(JsonNode.Parse("3")).ShouldBe(3f);
    }

    [Fact]
    public void A_number_out_of_range_is_held_to_it()
    {
        Voices().Value(JsonValue.Create(99)).ShouldBe(8f);
    }

    [Fact]
    public void Anything_but_a_number_is_the_default()
    {
        var voices = Voices();

        voices.Value(JsonValue.Create("3")).ShouldBe(1f);
        voices.Value(null).ShouldBe(1f);
    }
}
