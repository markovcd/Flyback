using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Codex.Tests;

/// <summary>The command line Codex is run with, and its output read back.</summary>
public class CliTests
{
    private const string Instructions = "/tmp/instructions.md";

    private static CodexRequest Request(
        string model = "gpt-5.5", AssistantEffort effort = AssistantEffort.Medium, params byte[][] pictures) =>
        new(model, effort, "hi", pictures);

    private static List<string> Arguments(CodexRequest request, params string[] pictures) =>
        [.. CodexCli.Arguments(request, Instructions, pictures)];

    private static string After(List<string> arguments, string flag) => arguments[arguments.IndexOf(flag) + 1];

    [Fact]
    public void The_program_runs_headless_in_a_read_only_sandbox_and_reads_neither_configuration_nor_rules()
    {
        var arguments = Arguments(Request());

        arguments.Take(2).ShouldBe(["exec", "-"]);
        arguments.ShouldContain("--json");
        arguments.ShouldContain("--ephemeral");
        arguments.ShouldContain("--skip-git-repo-check");
        arguments.ShouldContain("--ignore-user-config");
        arguments.ShouldContain("--ignore-rules");
        After(arguments, "--sandbox").ShouldBe("read-only");
    }

    [Fact]
    public void Its_instructions_are_replaced_and_every_tool_skill_and_search_is_off()
    {
        var arguments = Arguments(Request());

        arguments.ShouldContain($"model_instructions_file=\"{Instructions}\"");
        arguments.ShouldContain("web_search=\"disabled\"");
        arguments.ShouldContain("skills.include_instructions=false");
        arguments.ShouldContain("include_environment_context=false");
        arguments.ShouldContain("project_doc_max_bytes=0");
        arguments.ShouldContain("features.shell_tool=false");
        arguments.ShouldContain("features.unified_exec=false");
        arguments.ShouldContain("features.apps=false");
        arguments.ShouldContain("features.plugins=false");
    }

    [Fact]
    public void The_model_is_passed_unless_it_is_left_to_codex_and_effort_always()
    {
        var plain = Arguments(Request("gpt-5.6-terra"));

        After(plain, "--model").ShouldBe("gpt-5.6-terra");
        plain.ShouldContain("model_reasoning_effort=\"medium\"");

        Arguments(Request(CodexCli.DefaultModel)).ShouldNotContain("--model");
        Arguments(Request(effort: AssistantEffort.Low)).ShouldContain("model_reasoning_effort=\"low\"");
        Arguments(Request(effort: AssistantEffort.High)).ShouldContain("model_reasoning_effort=\"high\"");
    }

    [Theory]
    [InlineData("--dangerously-bypass-approvals-and-sandbox")]
    [InlineData("gpt-5.5 --sandbox danger-full-access")]
    [InlineData("")]
    [InlineData("-m")]
    public void A_model_name_that_could_read_as_a_flag_never_reaches_the_command_line(string model)
    {
        Should.Throw<ProgramFailure>(() => CodexCli.Arguments(Request(model), Instructions, []));
    }

    [Theory]
    [InlineData("gpt-5.5")]
    [InlineData("gpt-5.6-terra")]
    [InlineData("default")]
    public void Ids_and_the_default_are_models(string model) => CodexCli.IsModel(model).ShouldBeTrue();

    [Fact]
    public void Pictures_end_the_line_so_none_of_them_can_take_the_prompt()
    {
        var arguments = Arguments(Request(), "/tmp/a.png", "/tmp/b.png");

        arguments.TakeLast(3).ShouldBe(["--image", "/tmp/a.png", "/tmp/b.png"]);
        arguments.IndexOf("-").ShouldBeLessThan(arguments.IndexOf("--image"));
    }

    [Fact]
    public void A_path_is_a_toml_string_whatever_it_holds()
    {
        CodexCli.Toml(@"C:\Users\O""Brien\i.md").ShouldBe("\"C:\\\\Users\\\\O\\\"Brien\\\\i.md\"");
        Should.Throw<ProgramFailure>(() => CodexCli.Toml("a\nb"));
    }

    [Fact]
    public void The_last_message_is_the_answer_and_its_usage_is_the_cost()
    {
        const string Output = """
            {"type":"thread.started","thread_id":"t"}
            {"type":"item.completed","item":{"id":"item_0","type":"error","message":"Codex is ignoring 1 unrecognized configuration setting."}}
            {"type":"turn.started"}
            {"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"early"}}
            {"type":"item.completed","item":{"id":"item_2","type":"agent_message","text":"Done."}}
            {"type":"turn.completed","usage":{"input_tokens":1000,"cached_input_tokens":900,"output_tokens":7}}
            """;

        var answer = CodexCli.Answer(Output, string.Empty, 0);

        answer.Text.ShouldBe("Done.");
        answer.Input.ShouldBe(1000);
        answer.Cached.ShouldBe(900);
        answer.Output.ShouldBe(7);
    }

    [Fact]
    public void Being_signed_out_says_what_to_do()
    {
        var failure = Should.Throw<ProgramFailure>(() => CodexCli.Answer(
            """{"type":"turn.failed","error":{"message":"401 Unauthorized: please log in again"}}""", string.Empty, 1));

        failure.Message.ShouldContain("codex login");
    }

    [Fact]
    public void A_usage_limit_is_passed_on_as_it_was_said_and_is_not_taken_for_being_signed_out()
    {
        var failure = Should.Throw<ProgramFailure>(() => CodexCli.Answer(
            """
            {"type":"error","message":"You have hit your usage limit. Try again at 3:34 PM."}
            {"type":"turn.failed","error":{"message":"You have hit your usage limit. Try again at 3:34 PM."}}
            """, string.Empty, 1));

        failure.Message.ShouldBe("Codex failed: You have hit your usage limit. Try again at 3:34 PM.");
    }

    [Fact]
    public void No_message_at_all_reports_what_was_written_to_the_error_stream()
    {
        Should.Throw<ProgramFailure>(() => CodexCli.Answer("garbage", "boom", 2))
            .Message.ShouldContain("boom");
    }

    /// <summary>
    /// A real process: a stand-in program that reports whether it was handed a key,
    /// what the input was and which pictures it was named, standing where
    /// <c>codex</c> would.
    /// </summary>
    [Fact]
    public async Task A_key_in_the_environment_is_not_passed_on_and_the_prompt_and_pictures_arrive()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the stand-in is a shell script");

        var folder = Directory.CreateTempSubdirectory("flyback-codex-stand-in").FullName;
        var script = Path.Combine(folder, "codex");

        await File.WriteAllTextAsync(script, """
            #!/bin/sh
            input=$(cat)
            case "$input" in hi) heard=heard;; *) heard=silent;; esac
            seen=none
            for a in "$@"; do case "$a" in *picture-1.png) [ -s "$a" ] && seen=picture;; esac; done
            printf '{"type":"item.completed","item":{"id":"i","type":"agent_message","text":"%s,%s,key=%s,other=%s"}}\n' "$heard" "$seen" "${CODEX_API_KEY:-none}" "${OPENAI_API_KEY:-none}"
            printf '{"type":"turn.completed","usage":{}}\n'
            """.ReplaceLineEndings("\n"), TestContext.Current.CancellationToken);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var codex = Environment.GetEnvironmentVariable("CODEX_API_KEY");
        var openai = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", "sk-fake-for-this-test");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-fake-for-this-test");

            var answer = await new CodexCli(script).Ask(Request(pictures: [[1, 2, 3]]), TestContext.Current.CancellationToken);

            answer.Text.ShouldBe("heard,picture,key=none,other=none");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", codex);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", openai);
            Directory.Delete(folder, true);
        }
    }
}
