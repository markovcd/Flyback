using Flyback.Host;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>A command that opens a shipped preset by name also lists the names it takes.</summary>
public sealed class PatchInputTests
{
    private static readonly PluginRegistry Registry = new(() => PluginCatalog.Empty, "nowhere", null);

    [Fact]
    public void Every_command_with_preset_lists_the_presets_too()
    {
        var all = Program.Commands(Registry, new OutputSettings());
        var commands = all.Subcommands.Where(c => c.Options.Any(o => o.Name == "--preset")).ToList();

        commands.Count.ShouldBeGreaterThan(5);
        commands.Where(c => c.Options.All(o => o.Name != "--presets")).Select(c => c.Name).ShouldBeEmpty();
    }
}
