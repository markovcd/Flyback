using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <c>flyback-cli viewer</c> hands everything to the other program and hands its exit
/// code back. A script stands in for the viewer, so what is asserted is what arrived.
/// </summary>
/// <remarks>
/// Not run in parallel with anything else that changes <see cref="ViewerCommand.Beside"/>,
/// which is one seam for the whole process.
/// </remarks>
[Collection("viewer")]
public class ViewerCommandTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-viewer-cmd-" + Guid.NewGuid().ToString("N"));
    private readonly Func<string> before = ViewerCommand.Beside;
    private readonly string record;

    public ViewerCommandTests()
    {
        Directory.CreateDirectory(folder);
        record = Path.Combine(folder, "args.txt");

        Environment.SetEnvironmentVariable("FLYBACK_TEST_ARGS", record);
    }

    public void Dispose()
    {
        ViewerCommand.Beside = before;
        Environment.SetEnvironmentVariable("FLYBACK_TEST_ARGS", null);
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>A program that writes each argument it got on a line of its own and exits with <paramref name="code"/>.</summary>
    private string Script(int code)
    {
        string path;

        if (OperatingSystem.IsWindows())
        {
            path = Path.Combine(folder, "viewer.cmd");
            File.WriteAllText(path, $"@echo off\r\n:next\r\nif \"%~1\"==\"\" goto done\r\necho %~1>>\"%FLYBACK_TEST_ARGS%\"\r\nshift\r\ngoto next\r\n:done\r\nexit /b {code}\r\n");
        }
        else
        {
            path = Path.Combine(folder, "viewer.sh");
            File.WriteAllText(path, $"#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> \"$FLYBACK_TEST_ARGS\"; done\nexit {code}\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        ViewerCommand.Beside = () => path;

        return path;
    }

    private string[] Arrived() => File.Exists(record) ? File.ReadAllLines(record) : [];

    [Fact]
    public void Claims_only_a_line_that_starts_with_the_word()
    {
        ViewerCommand.Claims(["viewer"]).ShouldBeTrue();
        ViewerCommand.Claims(["viewer", "--help"]).ShouldBeTrue();
        ViewerCommand.Claims([]).ShouldBeFalse();
        ViewerCommand.Claims(["render", "viewer"]).ShouldBeFalse();
        ViewerCommand.Claims(["Viewer"]).ShouldBeFalse();
    }

    [Fact]
    public void Every_token_arrives_as_it_was_typed_including_help_and_what_looks_like_a_cli_flag()
    {
        Script(0);

        string[] tokens = ["--help", "--preset", "Dub", "--out", "--json", "-o", "nebula.fbk", "--hidden"];

        ViewerCommand.Run(tokens, new StringWriter()).ShouldBe(0);

        Arrived().ShouldBe(tokens);
    }

    [Fact]
    public void The_viewers_exit_code_is_the_answer()
    {
        Script(7);

        ViewerCommand.Run(["--for", "1"], new StringWriter()).ShouldBe(7);
    }

    [Fact]
    public void A_viewer_that_is_not_there_says_where_it_looked_and_does_not_throw()
    {
        var missing = Path.Combine(folder, "nowhere", "flyback-viewer");

        ViewerCommand.Beside = () => missing;

        var error = new StringWriter();

        ViewerCommand.Run(["--help"], error).ShouldBe(Exit.Failed);

        error.ToString().ShouldContain(missing);
    }

    [Fact]
    public void The_command_is_registered_whether_or_not_the_viewer_is_there()
    {
        var command = ViewerCommand.Build();

        command.Name.ShouldBe("viewer");
        command.Description.ShouldNotBeNullOrWhiteSpace();
        command.TreatUnmatchedTokensAsErrors.ShouldBeFalse();
    }
}
