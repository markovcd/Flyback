using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Programs.Tests;

/// <summary>What is said when a program fails, and how a turn's cost is read.</summary>
public class ReasonTests
{
    private static readonly Regex SignedOut = new("not logged in", RegexOptions.IgnoreCase);

    private static string Explained(string said, string errors) =>
        ProgramReason.Explained(said, errors, "Thing", SignedOut, "Sign in.");

    [Fact]
    public void What_it_said_is_the_reason_and_the_error_stream_is_the_fallback()
    {
        Explained("It broke.", "ignored").ShouldBe("Thing failed: It broke.");
        Explained("", " boom ").ShouldBe("Thing failed: boom");
        Explained("", "").ShouldBe("Thing exited without answering.");
    }

    [Fact]
    public void Being_signed_out_says_what_to_do_and_keeps_the_reason()
    {
        Explained("Not logged in.", "").ShouldBe("Sign in. Not logged in.");
    }

    [Fact]
    public void A_long_reason_is_cut()
    {
        Explained(new string('x', 700), "").Length.ShouldBeLessThan(640);
    }

    [Fact]
    public void A_count_that_is_missing_or_not_a_number_is_zero()
    {
        var usage = JsonNode.Parse("""{"a":5,"b":"six"}""");

        Tokens.Count(usage, "a").ShouldBe(5);
        Tokens.Count(usage, "b").ShouldBe(0);
        Tokens.Count(usage, "c").ShouldBe(0);
        Tokens.Count(null, "a").ShouldBe(0);
    }
}
