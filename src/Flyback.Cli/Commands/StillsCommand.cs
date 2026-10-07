using System.CommandLine;
using System.Text;
using Flyback.Cli.Common;
using Flyback.Cli.Rendering;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// Draws the still of every preset this build offers, with the index the editor, the
/// web editor and the web viewer read them by (ADR-0163).
/// </summary>
internal static class StillsCommand
{
    /// <summary>
    /// WebP rather than JPEG, which rings around a still's sharp edges at this size, and
    /// rather than PNG, which a gallery downloading sixty of them notices.
    /// </summary>
    private const string Quality = "90";

    public static Command Build(PluginRegistry plugins)
    {
        var output = new Option<DirectoryInfo>("--out", "-o")
        {
            Description = $"The folder to write the stills and {StillIndex.FileName} into.",
            Required = true,
        };

        var ffmpeg = new Option<string>("--ffmpeg")
        {
            Description = "The ffmpeg to encode the stills with. Left out, the first on PATH is used.",
        };

        var command = new Command("stills", "Draw a still of every preset, for the galleries to show instead of drawing them.")
        {
            output, ffmpeg,
        };

        command.SetAction((result, cancellation) => Run(
            plugins,
            result.GetRequiredValue(output),
            result.GetValue(ffmpeg),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
            cancellation));

        return command;
    }

    internal static async Task<int> Run(PluginRegistry plugins, DirectoryInfo folder, string? ffmpeg, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        if (Ffmpeg.Resolve(ffmpeg) is not { } found)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: the stills are WebP, which ffmpeg encodes, and there is no ffmpeg on PATH. Install it or point --ffmpeg at it.");
            return Exit.Failed;
        }

        var tools = new PresetTools(found, TimeSpan.FromMinutes(1));

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
            var (entry, pixels) = Draw(preset, plugins.Catalog.Modules, taken);

            if (pixels is not null && await Encode(tools, pixels, Path.Combine(folder.FullName, entry.File!), cancellation) is { } why)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {preset.Name}: ffmpeg did not write its still. {why}");
                return Exit.Failed;
            }

            entries.Add(entry);
            output.WriteLine($"{preset.Name}: {Said(entry.Still)}");
        }

        File.WriteAllText(Path.Combine(folder.FullName, StillIndex.FileName), new StillIndex(StillIndex.ThisBuild, entries).Write());

        return Exit.Ok;
    }

    /// <summary>The preset's entry, and the pixels of its still where it has one, named in the entry.</summary>
    private static (StillEntry Entry, byte[]? Pixels) Draw(PatchPreset preset, ModuleCatalog modules, HashSet<string> taken)
    {
        try
        {
            var patch = preset.Build(modules);
            var within = preset.Files is { } files ? new BundleFiles(files()) : null;
            var (kind, pixels) = PresetStill.Draw(patch, within, within, program => IlCompiler.CompileOnce(program, IlParts.Staged));
            var file = pixels is null ? null : Unique(Slug(preset.Name), taken) + ".webp";

            return (new StillEntry(preset.Name, preset.Kind, kind, file, patch.Description, patch.Author, patch.Tags, patch.Reaches().Sound), pixels);
        }
        catch (Exception)
        {
            return (new StillEntry(preset.Name, preset.Kind, StillKind.Unavailable, null, preset.Description), null);
        }
    }

    /// <summary>The still as WebP at <paramref name="to"/>, or what ffmpeg said where it did not write it.</summary>
    private static async Task<string?> Encode(IPresetTools tools, byte[] pixels, string to, CancellationToken cancellation)
    {
        var png = to + ".png";
        PngWriter.WriteBgra(png, pixels, PresetStill.Width, PresetStill.Height, PresetStill.Width * 4);

        try
        {
            var ran = await tools.Ffmpeg(["-y", "-loglevel", "error", "-i", png, "-c:v", "libwebp", "-quality", Quality, "-compression_level", "6", to], cancellation);

            return ran.Ok && File.Exists(to) ? null : ran.Error.Trim();
        }
        finally
        {
            File.Delete(png);
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
