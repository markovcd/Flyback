using System.CommandLine;
using System.Text;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Draws the still of every preset this build offers, with the index the editor, the
/// web editor and the web viewer read them by (ADR-0163).
/// </summary>
internal static class StillsCommand
{
    /// <summary>A JPEG a fifth the size of the PNG, which a gallery downloading sixty of them notices.</summary>
    private static readonly JpegWriter Jpeg = new();

    public static Command Build(PluginRegistry plugins)
    {
        var output = new Option<DirectoryInfo>("--out", "-o")
        {
            Description = $"The folder to write the stills and {StillIndex.FileName} into.",
            Required = true,
        };

        var command = new Command("stills", "Draw a still of every preset, for the galleries to show instead of drawing them.")
        {
            output,
        };

        command.SetAction(result => Run(plugins, result.GetRequiredValue(output), result.InvocationConfiguration.Output, result.InvocationConfiguration.Error));

        return command;
    }

    internal static int Run(PluginRegistry plugins, DirectoryInfo folder, TextWriter output, TextWriter error)
    {
        try
        {
            folder.Create();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {folder.FullName}: {e.Message}");
            return Exit.Failed;
        }

        var entries = new List<StillEntry>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in plugins.Catalog.Presets)
        {
            var entry = Draw(preset, plugins.Catalog.Modules, folder, taken);

            entries.Add(entry);
            output.WriteLine($"{preset.Name}: {Said(entry.Still)}");
        }

        File.WriteAllText(Path.Combine(folder.FullName, StillIndex.FileName), new StillIndex(StillIndex.ThisBuild, entries).Write());

        return Exit.Ok;
    }

    private static StillEntry Draw(PatchPreset preset, ModuleCatalog modules, DirectoryInfo folder, HashSet<string> taken)
    {
        try
        {
            var patch = preset.Build(modules);
            var within = preset.Files is { } files ? new BundleFiles(files()) : null;
            var (kind, pixels) = PresetStill.Draw(patch, within, within, program => IlCompiler.CompileOnce(program, IlParts.Staged));

            string? file = null;

            if (pixels is not null)
            {
                file = Unique(Slug(preset.Name), taken) + ".jpg";

                using var written = File.Create(Path.Combine(folder.FullName, file));
                Jpeg.WriteBgra(written, pixels, PresetStill.Width, PresetStill.Height, PresetStill.Width * 4);
            }

            return new StillEntry(preset.Name, preset.Kind, kind, file, patch.Description, patch.Author, patch.Tags);
        }
        catch (Exception)
        {
            return new StillEntry(preset.Name, preset.Kind, StillKind.Unavailable, null, preset.Description);
        }
    }

    private static string Said(StillKind kind) => kind switch
    {
        StillKind.Picture => "drawn",
        StillKind.SoundOnly => "sound only",
        StillKind.Nothing => "nothing to draw",
        _ => "would not draw",
    };

    /// <summary>A file name that reads as the preset's name: lower case, words joined by hyphens.</summary>
    private static string Slug(string name)
    {
        var slug = new StringBuilder();

        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        return slug.ToString().Trim('-') is { Length: > 0 } made ? made : "preset";
    }

    private static string Unique(string slug, HashSet<string> taken)
    {
        var name = slug;

        for (var n = 2; !taken.Add(name); n++) name = $"{slug}-{n}";

        return name;
    }
}
