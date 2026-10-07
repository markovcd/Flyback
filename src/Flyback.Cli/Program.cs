using System.CommandLine;
using System.CommandLine.Completions;
using System.Text;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli;

/// <summary>
/// The second shell over the engine. Everything here is argument parsing and where to
/// write the answer; the work is Core's, exactly as it is for the window.
/// </summary>
/// <remarks>
/// A separate program rather than a mode of the shell, for what it does not carry: no
/// Avalonia, so no X libraries, no fonts and no display on the machine that runs it.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        // The sentences this prints are the engine's own, em-dashes and all, and
        // a Windows console left on its system codepage turns those into
        // something else. Attempted rather than assumed: there is not always a
        // console to have an encoding.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console. Whatever is reading this can have the default.
        }

        // The viewer is a program of its own, so it is handed the rest of the line
        // before there is anything to load or parse: its --help is its own.
        if (ViewerCommand.Claims(args)) return ViewerCommand.Run(args[1..], Console.Error);

        var plugins = new PluginRegistry(
            PluginHost.Load,
            PluginHost.DefaultDirectory,

            // The report only where somebody is watching, the way the editor keeps it to a
            // terminal it inherited: a script reading stderr wants the command's complaint
            // and nothing else.
            Console.IsErrorRedirected ? null : Console.Error);

        return Run(args, plugins, new InvocationConfiguration());
    }

    /// <summary>The commands, and what the shell is told the chosen one did.</summary>
    internal static int Run(string[] args, PluginRegistry plugins, InvocationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var json = new Option<bool>("--json") { Description = "Write the answer as JSON instead of prose." };

        var exports = ExportDefaults.Load(ExportDefaults.PathIn(args) ?? SettingsFile.Path);

        var root = new RootCommand($"{GlobalConstants.ApplicationName} — a patchable synthesiser, from the command line.")
        {
            RenderCommand.Build(plugins, exports),
            CheckCommand.Build(plugins, json),
            InfoCommand.Build(plugins, json),
            PrintCommand.Build(plugins),
            PackCommand.Build(plugins, json),
            SaveCommand.Build(plugins),
            PackPluginCommand.Build(),
            PluginKeyCommand.Build(),
            PluginCommand.Build(plugins, json),
            ModulesCommand.Build(plugins, json),
            CompareCommand.Build(plugins, json),
            ProbeCommand.Build(plugins, json),
            DecideCommand.Build(plugins, json),
            MeasureCommand.Build(plugins, json),
            AskCommand.Build(plugins, json),
            ViewerCommand.Build(),
            ShotCommand.Build(plugins),
            RenderPresetsCommand.Build(plugins),
            StillsCommand.Build(plugins),
        };

        // What dotnet-suggest asks for completions with, and the only reason the
        // shell can finish a command this program has.
        var suggest = new SuggestDirective();

        root.Add(suggest);

        var parsed = root.Parse(args);
        var code = parsed.Invoke(configuration);

        // Invoked either way, because that is what prints the complaint and the
        // help beneath it. But an argument nobody could parse is the shell being
        // held wrong rather than a patch being wrong, and the two should not
        // come back as the same number. Half-typed input is neither: it is what
        // a completion is asked about.
        return parsed.Errors.Count > 0 && parsed.GetResult(suggest) is null ? Exit.Failed : code;
    }
}
