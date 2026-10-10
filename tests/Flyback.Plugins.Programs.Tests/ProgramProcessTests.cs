using Shouldly;
using Xunit;

namespace Flyback.Plugins.Programs.Tests;

/// <summary>How a program is started.</summary>
public class ProgramProcessTests
{
    [Fact]
    public void A_program_gets_no_console_window_of_its_own()
    {
        var start = ProgramProcess.StartInfo("claude", ["-p"], Path.GetTempPath(), []);

        start.CreateNoWindow.ShouldBeTrue();
        start.UseShellExecute.ShouldBeFalse();
    }

    [Fact]
    public void The_variables_it_is_not_given_are_taken_out_of_its_environment()
    {
        Environment.SetEnvironmentVariable("FLYBACK_TEST_KEY", "secret");

        try
        {
            var start = ProgramProcess.StartInfo("claude", [], Path.GetTempPath(), ["FLYBACK_TEST_KEY"]);

            start.Environment.ContainsKey("FLYBACK_TEST_KEY").ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLYBACK_TEST_KEY", null);
        }
    }
}
