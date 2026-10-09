using System.CommandLine;
using System.Text.RegularExpressions;
using Flyback.Host;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// Every command flyback-cli has, and every command under one, is named as <c>flyback-cli</c> and its path by
/// a feature in <c>tests/Flyback.Specs</c>, where what it does is stated.
/// </summary>
public sealed class CommandScenarioTests
{
    /// <summary>Every feature's text, as one.</summary>
    private static readonly string Features = string.Join(
        '\n', Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Features"), "*.feature").Select(File.ReadAllText));

    /// <summary>
    /// The commands no feature named when this rule was made. One leaves the list in the commit that gives it a
    /// scenario, and none joins it: a command added since is named by a feature before it lands.
    /// </summary>
    private static readonly HashSet<string> Unwritten =
    [
        "pack-plugin", "plugin-key", "probe", "viewer",
    ];

    public static TheoryData<string> Paths => [.. CommandPaths()];

    [Theory]
    [MemberData(nameof(Paths))]
    public void Every_command_is_named_by_a_feature(string path)
    {
        var named = Regex.IsMatch(Features, $@"flyback-cli {Regex.Escape(path)}(?![\w-])");

        if (Unwritten.Contains(path))
            named.ShouldBeFalse($"A feature names flyback-cli {path} now: take it off {nameof(Unwritten)}.");
        else
            named.ShouldBeTrue($"No feature names flyback-cli {path}: state what it does in a scenario in tests/Flyback.Specs, and name it there.");
    }

    [Fact]
    public void Every_command_left_unwritten_is_still_there() =>
        Unwritten.Except(CommandPaths()).ShouldBeEmpty();

    /// <summary>Each command's path below the root, a command with commands of its own followed by each of them.</summary>
    private static IEnumerable<string> CommandPaths()
    {
        static IEnumerable<string> Below(Command command, string above) =>
            command.Subcommands.SelectMany(c => (string[])[$"{above}{c.Name}", .. Below(c, $"{above}{c.Name} ")]);

        return Below(Program.Commands(new PluginRegistry(() => PluginCatalog.Empty, "nowhere", null), new OutputSettings()), "");
    }
}
