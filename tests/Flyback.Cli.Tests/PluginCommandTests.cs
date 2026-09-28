using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Tests;

/// <summary><c>plugin allow</c>, <c>deny</c> and <c>list</c>: which plugin folders load, from a script.</summary>
public sealed class PluginCommandTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-cli-trust-{Guid.NewGuid():N}")).FullName;

    private int scans;

    private string Plugins => Path.Combine(root, PluginHost.DirectoryName);

    private PluginAllowances Allowances => new(Path.Combine(root, "allowed-plugins.json"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_folder_copied_in_by_hand_is_listed_as_not_yet_allowed()
    {
        var mine = CopiedIn("Mine");

        var (code, output, _) = Run("plugin", "list");

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("Mine  not allowed");
        output.ShouldContain($"flyback-cli plugin allow \"{mine}\"");
    }

    [Fact]
    public void Allowing_a_folder_by_name_loads_it_as_it_stands()
    {
        var mine = CopiedIn("Mine");

        var (code, output, _) = Run("plugin", "allow", "Mine");

        code.ShouldBe(Exit.Ok);
        output.ShouldContain($"Allowed Mine in {mine}, as its 1 file stand now.");

        Folders()[0].GetProperty("standing").GetString().ShouldBe("allowed");
        Folders()[0].GetProperty("secrets").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Keeping_keys_is_allowed_only_when_asked_for()
    {
        CopiedIn("Mine");

        Run("plugin", "allow", "Mine", "--secrets").Code.ShouldBe(Exit.Ok);

        Folders()[0].GetProperty("secrets").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void A_folder_changed_after_it_was_allowed_says_which_file()
    {
        var mine = CopiedIn("Mine");

        Run("plugin", "allow", mine);
        File.WriteAllText(Path.Combine(mine, "Mine.dll"), "rebuilt");

        var folder = Folders()[0];

        folder.GetProperty("standing").GetString().ShouldBe("changed");
        folder.GetProperty("loads").GetBoolean().ShouldBeFalse();
        folder.GetProperty("reason").GetString().ShouldStartWith("Mine.dll has changed since this plugin was allowed");
    }

    [Fact]
    public void Denying_takes_the_yes_back_and_denying_nothing_fails()
    {
        CopiedIn("Mine");

        Run("plugin", "allow", "Mine");

        Run("plugin", "deny", "Mine").Code.ShouldBe(Exit.Ok);
        Folders()[0].GetProperty("standing").GetString().ShouldBe("not-allowed");

        var (code, _, error) = Run("plugin", "deny", "Mine");

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("nothing was allowed for");
    }

    [Fact]
    public void A_folder_with_no_plugin_in_it_is_not_allowed()
    {
        Directory.CreateDirectory(Path.Combine(Plugins, "Empty"));

        var (code, _, error) = Run("plugin", "allow", "Empty");

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("there is no plugin in");
        File.Exists(Allowances.File).ShouldBeFalse();
    }

    [Fact]
    public void Saying_which_folders_load_loads_none_of_them()
    {
        CopiedIn("Mine");

        Run("plugin", "list");
        Run("plugin", "allow", "Mine");

        scans.ShouldBe(0);
    }

    private JsonElement[] Folders()
    {
        var (code, output, _) = Run("plugin", "list", "--json");

        code.ShouldBe(Exit.Ok);

        return [.. JsonDocument.Parse(output).RootElement.GetProperty("folders").EnumerateArray()];
    }

    /// <summary>A plugin folder with one file in it: the hash is all that is checked here, and nothing is loaded.</summary>
    private string CopiedIn(string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Plugins, name)).FullName;

        File.WriteAllText(Path.Combine(folder, $"{name}.dll"), "not really an assembly");

        return folder;
    }

    private (int Code, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var registry = new PluginRegistry(
            () =>
            {
                scans++;
                return PluginCatalog.Empty;
            },
            Plugins,
            null,
            () => new PluginTrust(true, ShippedList.Beside(Plugins), Allowances));

        var code = Program.Run(args, registry, new InvocationConfiguration { Output = output, Error = error });

        return (code, output.ToString(), error.ToString());
    }
}
