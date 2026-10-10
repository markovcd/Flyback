using System.Diagnostics;
using System.Text;
using Avalonia;
using Flyback.Ui;
using Flyback.Ui.Audio;
using Flyback.Core;
using Flyback.Plugins.Hosting;
using Flyback.Host;

namespace Flyback.Viewer.Desktop;

/// <summary>
/// The third program over the engine: opens a patch, plays it, and writes nothing.
/// </summary>
/// <remarks>
/// Not the editor with a flag, because what the editor keeps — settings, layout, a
/// recovery file, statistics — is exactly what a look at a patch must not leave behind.
/// It only reads: the plugin folder and the settings the editor saved.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Only a console somebody is looking at is worth writing to; see Terminal.
        if (Terminal.Inherited)
        {
            Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));
            Trace.AutoFlush = true;
        }
        else
        {
            Terminal.Release();
        }

        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console. Whatever is reading this can have the default.
        }

        // A plugin package is the editor's to install (ADR-0132), whichever program the
        // system opens it with.
        if (args.FirstOrDefault(a => !a.StartsWith('-')) is { } first
            && first.EndsWith(".fbkp", StringComparison.OrdinalIgnoreCase))
            return HandToEditor(first);

        // Read before the command is built, so --help says what this machine is set to.
        var settings = OutputSettings.Load(SettingsFlag.PathIn(args) ?? SettingsFile.Path);

        // Before a patch is read: it may name modules only a plugin defines. Loading
        // is a read; nothing here calls what would write a file.
        var plugins = PluginHost.Load().Install();

        foreach (var line in PluginReport.Lines(plugins, PluginHost.DefaultDirectory)) Trace.WriteLine(line);

        var root = ViewerArguments.Build(settings, options => Play(options, settings, plugins), Console.Error);

        var parsed = root.Parse(args);
        var code = parsed.Invoke();

        // A flag nobody could parse is the shell held wrong, not a patch that would not play.
        return parsed.Errors.Count > 0 ? Exit.Failed : code;
    }

    private static int Play(ViewerOptions options, OutputSettings settings, PluginCatalog plugins)
    {
        var library = new PresetLibrary(PresetLibrary.DefaultFolder);

        if (options.ListPresets)
        {
            foreach (var preset in PresetLibrary.Ordered(plugins.Presets, library)) Console.Out.WriteLine(preset.Name);

            return Exit.Ok;
        }

        if (ViewerSource.Resolve(options, settings, plugins, library, Console.Error) is not var (opened, name))
            return Exit.Failed;

        settings.LatencyMilliseconds = options.LatencyMilliseconds;

        if (options.Trace is { } trace)
        {
            try
            {
                StallTrace.Open(trace);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: --trace {trace}: {ex.Message}");

                return Exit.Failed;
            }
        }

        var sound = Speakers(options, settings, plugins);

        ViewerApp.Launch = new ViewerLaunch(
            opened,
            sound?.Device,
            options with { Title = options.Title ?? $"Flyback Viewer — {name}" },
            settings.Takeover)
        {
            Instruments = plugins.PreferredMidiInput,
            Plugins = plugins,
            Settings = settings,
            Output = sound?.Output,
        };

        try
        {
            return AppBuilder.Configure<ViewerApp>()
                .UsePlatformDetect()
                .UseDriver(settings.Driver)
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }
        finally
        {
            StallTrace.Close();
        }
    }

    private static int HandToEditor(string package)
    {
        var editor = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Flyback.exe" : "Flyback");

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(editor) { ArgumentList = { package }, UseShellExecute = false });
            return Exit.Ok;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: {Path.GetFileName(package)} is a plugin, and the editor that installs it did not start: {ex.Message}");
            return Exit.Failed;
        }
    }

    /// <summary>The device to play through and its backend, or null where none was asked for or none could be had.</summary>
    private static AudioSetup? Speakers(ViewerOptions options, OutputSettings settings, PluginCatalog plugins)
    {
        if (options.NoAudio) return null;

        var setup = Sound.Open(plugins, settings);

        if (setup.Failure is { } failure) Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: no sound — {failure}");

        if (setup.Output is not null) return setup;

        setup.Device.Dispose();

        if (setup.Failure is null)
            Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: no sound — no audio plugin was found beside the program.");

        return null;
    }
}
