using System.CommandLine;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <see cref="Plugins"/> — the plugin folder, scanned for the commands that need it.
/// </summary>
/// <remarks>
/// A scan costs an assembly load per plugin and puts a report on stderr, and neither
/// belongs to somebody asking for help, finishing a half-typed line or making a key.
/// What has to hold is that the ones that read a patch still scan before they do.
/// </remarks>
public class PluginsTests
{
    /// <summary>A catalog that counts how often it was asked for, standing in for the folder.</summary>
    private sealed class Folder
    {
        public int Scans { get; private set; }

        public StringWriter Report { get; } = new();

        public Plugins Plugins => plugins ??= new Plugins(Scan, "nowhere", Report);

        private Plugins? plugins;

        private PluginCatalog Scan()
        {
            Scans++;
            return PluginCatalog.Empty;
        }
    }

    private static (int Scans, string Report) Ran(params string[] args)
    {
        var folder = new Folder();

        Program.Run(
            args,
            folder.Plugins,
            new InvocationConfiguration { Output = TextWriter.Null, Error = TextWriter.Null });

        return (folder.Scans, folder.Report.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("render", "--help")]
    [InlineData("pack-plugin", "--help")]
    [InlineData("plugin-key", "--help")]
    [InlineData("nonesuch")]
    public void A_command_line_that_asks_nothing_of_a_plugin_leaves_the_folder_alone(params string[] args) =>
        Ran(args).Scans.ShouldBe(0);

    /// <summary>
    /// The other half of it: a patch may name a module only a plugin defines, so
    /// anything that reads one scans first, whether or not the file is there.
    /// </summary>
    [Theory]
    [InlineData("modules")]
    [InlineData("check", "nosuch.fbk")]
    [InlineData("info", "nosuch.fbk")]
    [InlineData("print", "nosuch.fbk")]
    public void A_command_that_reads_a_patch_or_lists_modules_scans_the_folder(params string[] args) =>
        Ran(args).Scans.ShouldBe(1);

    /// <summary>Where it looked and what it found, for whoever is watching the terminal.</summary>
    [Fact]
    public void The_scan_says_where_it_looked_and_what_it_found() =>
        Ran("modules").Report.ShouldContain("plugins: nowhere");

    [Fact]
    public void A_command_line_that_needs_no_plugin_says_nothing_about_them() =>
        Ran("--help").Report.ShouldBeEmpty();

    /// <summary>
    /// Once a run, however many times it is asked for: there is no reload, and a
    /// second report would be a second scan showing itself.
    /// </summary>
    [Fact]
    public void The_folder_is_scanned_once_however_often_it_is_asked_for()
    {
        var folder = new Folder();

        folder.Plugins.Ready();
        folder.Plugins.Ready();
        folder.Plugins.Catalog.ShouldBeSameAs(PluginCatalog.Empty);

        folder.Scans.ShouldBe(1);
        folder.Report.ToString().ShouldContain("nothing loaded");
    }

    /// <summary>The redirected-stderr case: a script reading it wants the command's complaint alone.</summary>
    [Fact]
    public void Without_somewhere_to_report_to_it_still_scans()
    {
        var scans = 0;

        var plugins = new Plugins(
            () =>
            {
                scans++;
                return PluginCatalog.Empty;
            },
            "nowhere",
            report: null);

        plugins.Ready();

        scans.ShouldBe(1);
    }
}
