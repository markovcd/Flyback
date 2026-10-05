using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.ClaudeCode.Tests;

/// <summary>The command line Claude Code is run with, and its output read back.</summary>
public class CliTests
{
    private static ClaudeRequest Request(string model = "sonnet", AssistantEffort effort = AssistantEffort.Medium) =>
        new(model, effort, [new JsonObject { ["type"] = "text", ["text"] = "hi" }]);

    [Fact]
    public void Every_tool_setting_and_server_is_off()
    {
        var arguments = ClaudeCli.Arguments(Request());

        arguments.ShouldContain("--print");
        arguments.ShouldContain("--strict-mcp-config");
        arguments.ShouldContain("--disable-slash-commands");
        arguments.ShouldContain("--no-session-persistence");
        arguments[arguments.ToList().IndexOf("--tools") + 1].ShouldBeEmpty();
        arguments[arguments.ToList().IndexOf("--setting-sources") + 1].ShouldBeEmpty();
    }

    [Fact]
    public void Every_process_runs_as_the_same_session_so_the_prompt_cache_is_hit()
    {
        var arguments = ClaudeCli.Arguments(Request());

        arguments[arguments.ToList().IndexOf("--session-id") + 1].ShouldBe(ClaudeCli.SessionId);
        Guid.TryParse(ClaudeCli.SessionId, out _).ShouldBeTrue();
    }

    [Fact]
    public void The_model_is_passed_and_effort_only_where_it_is_not_the_default()
    {
        var plain = ClaudeCli.Arguments(Request("opus"));

        plain[plain.ToList().IndexOf("--model") + 1].ShouldBe("opus");
        plain.ShouldNotContain("--effort");

        var high = ClaudeCli.Arguments(Request(effort: AssistantEffort.High));

        high[high.ToList().IndexOf("--effort") + 1].ShouldBe("high");
    }

    [Theory]
    [InlineData("--dangerously-skip-permissions")]
    [InlineData("sonnet --tools Bash")]
    [InlineData("")]
    [InlineData("-m")]
    public void A_model_name_that_could_read_as_a_flag_never_reaches_the_command_line(string model)
    {
        Should.Throw<ProgramFailure>(() => ClaudeCli.Arguments(Request(model)));
    }

    [Theory]
    [InlineData("sonnet")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("claude-opus-5-5[1m]")]
    public void Aliases_and_ids_are_models(string model) => ClaudeCli.IsModel(model).ShouldBeTrue();

    [Fact]
    public void The_conversation_is_one_line_of_input()
    {
        var line = ClaudeCli.Input(Request());

        line.ShouldNotContain('\n');
        JsonNode.Parse(line)!["message"]!["content"]![0]!["text"]!.GetValue<string>().ShouldBe("hi");
    }

    [Fact]
    public void The_result_line_is_the_answer_and_its_usage_is_the_cost()
    {
        const string Output = """
            {"type":"system","subtype":"init"}
            {"type":"assistant","message":{"content":[{"type":"text","text":"early"}]}}
            {"type":"result","is_error":false,"result":"Done.","usage":{"input_tokens":10,"cache_creation_input_tokens":5,"cache_read_input_tokens":900,"output_tokens":7}}
            """;

        var answer = ClaudeCli.Answer(Output, string.Empty, 0);

        answer.Text.ShouldBe("Done.");
        answer.Input.ShouldBe(15);
        answer.Cached.ShouldBe(900);
        answer.Output.ShouldBe(7);
    }

    [Fact]
    public void Being_signed_out_says_what_to_do()
    {
        var failure = Should.Throw<ProgramFailure>(() => ClaudeCli.Answer(
            """{"type":"result","is_error":true,"result":"Not logged in · Please run /login"}""", string.Empty, 1));

        failure.Message.ShouldContain("sign in with /login");
    }

    [Fact]
    public void No_result_at_all_reports_what_was_written_to_the_error_stream()
    {
        Should.Throw<ProgramFailure>(() => ClaudeCli.Answer("garbage", "boom", 2))
            .Message.ShouldContain("boom");
    }

    /// <summary>
    /// A real process: a stand-in program that reports whether it was handed a key
    /// and what the input was, standing where <c>claude</c> would.
    /// </summary>
    [Fact]
    public async Task A_key_in_the_environment_is_not_passed_on_and_the_input_arrives()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the stand-in is a shell script");

        var folder = Directory.CreateTempSubdirectory("flyback-claude-stand-in").FullName;
        var script = Path.Combine(folder, "claude");

        await File.WriteAllTextAsync(script, """
            #!/bin/sh
            input=$(cat)
            case "$input" in *'"text":"hi"'*) heard=heard;; *) heard=silent;; esac
            printf '{"type":"result","is_error":false,"result":"%s,key=%s","usage":{}}\n' "$heard" "${ANTHROPIC_API_KEY:-none}"
            """.ReplaceLineEndings("\n"), TestContext.Current.CancellationToken);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var before = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-fake-for-this-test");

            var answer = await new ClaudeCli(script).Ask(Request(), TestContext.Current.CancellationToken);

            answer.Text.ShouldBe("heard,key=none");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", before);
            Directory.Delete(folder, true);
        }
    }
}
