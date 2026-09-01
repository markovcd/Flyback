using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// <see cref="ExtraField.Choice"/> — a field that picks one of a list, where the
/// list is whatever the machine happens to have on it at the time.
/// </summary>
/// <remarks>
/// The one field whose tidying deliberately stops short of what it could do. A
/// stored id that is not in the list is not a broken value but a device that is
/// switched off, and the patch has to come back naming it — so what these pin is
/// mostly the refusal to correct: opening a patch on a machine without the
/// interface it was made on must not quietly rewrite it to mean something else.
/// </remarks>
public class ChoiceFieldTests
{
    private static ExtraField.Choice Ports() => new(
        "port",
        "MIDI in",
        [new ChoiceOption("iac", "IAC Driver"), new ChoiceOption("kbd", "Keystation")],
        Fallback: "none");

    [Fact]
    public void What_was_chosen_is_what_comes_back()
    {
        Ports().Value(JsonValue.Create("kbd")).ShouldBe("kbd");
    }

    /// <summary>
    /// The claim the whole field rests on: an id nothing in the list answers to
    /// survives, because the alternative rewrites somebody's patch the first time
    /// they open it with the interface unplugged.
    /// </summary>
    [Fact]
    public void A_device_that_is_not_here_keeps_its_name_rather_than_being_corrected()
    {
        var ports = Ports();

        ports.Value(JsonValue.Create("unplugged")).ShouldBe("unplugged");

        // And tidying the stored value leaves it alone too, which is what is
        // actually written back out to the file.
        ports.Sane(JsonValue.Create("unplugged")).GetValue<string>().ShouldBe("unplugged");
    }

    /// <summary>
    /// Nothing stored, something stored that is not a string, and a string with
    /// nothing in it are all "nothing was chosen" rather than three faults.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_value_that_says_nothing_falls_back(string? stored)
    {
        var node = stored is null ? null : JsonValue.Create(stored);

        Ports().Value(node).ShouldBe("none");
    }

    [Fact]
    public void A_value_of_the_wrong_kind_falls_back()
    {
        var ports = Ports();

        ports.Value(JsonValue.Create(7)).ShouldBe("none");
        ports.Value(JsonValue.Create(true)).ShouldBe("none");
    }

    [Fact]
    public void An_option_in_the_list_is_named_the_way_the_list_names_it()
    {
        Ports().Name("iac").ShouldBe("IAC Driver");
    }

    /// <summary>
    /// A device that has gone reads as its own id rather than as a blank, so the
    /// person looking at it can tell which one it was waiting for.
    /// </summary>
    [Fact]
    public void An_option_that_is_not_here_is_named_and_marked_as_absent()
    {
        Ports().Name("unplugged").ShouldBe("unplugged (not here)");
    }

    [Fact]
    public void Nothing_chosen_reads_as_nothing()
    {
        Ports().Name(string.Empty).ShouldBe("nothing");
    }

    /// <summary>
    /// What a person is shown is the stored value put through the list, so the
    /// two steps agree rather than being formatted separately.
    /// </summary>
    [Fact]
    public void What_is_shown_is_the_stored_value_named_by_the_list()
    {
        var ports = Ports();

        ports.Format(JsonValue.Create("kbd")).ShouldBe("Keystation");
        ports.Format(JsonValue.Create("unplugged")).ShouldBe("unplugged (not here)");

        // Nothing stored falls back first and is named second, and the fallback
        // here is not in the list either.
        ports.Format(null).ShouldBe("none (not here)");
    }
}
