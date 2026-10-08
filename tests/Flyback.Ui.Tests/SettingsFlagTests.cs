using Flyback.Host;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests;

/// <summary>The settings file a command line names, found before the command is built from what it holds.</summary>
public class SettingsFlagTests
{
    [Theory]
    [InlineData(new[] { "render", "a.fbk", "--settings", "x.json" }, "x.json")]
    [InlineData(new[] { "a.fbk", "--settings=y.json" }, "y.json")]
    [InlineData(new[] { "render", "a.fbk" }, null)]
    [InlineData(new[] { "--settings" }, null)]
    public void Another_settings_file_is_found_before_the_command_is_built(string[] args, string? path) =>
        SettingsFlag.PathIn(args).ShouldBe(path);
}
