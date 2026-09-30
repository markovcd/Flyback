using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;
using Flyback.Cli.Common;

namespace Flyback.Cli.Commands;

/// <summary>
/// Saves a patch, or a shipped preset, as whichever file the output's extension
/// names: a document, a bundle, or text in the language.
/// </summary>
/// <remarks>
/// A bundle is written by <see cref="PackCommand"/> and text by <see cref="PrintCommand"/>,
/// so each format has one writer. A preset's recordings live in the plugin rather than
/// on disk, so a document or a text saved from one names files nothing is beside; that
/// is written anyway and said, with <c>.fbkb</c> as the way to carry them.
/// </remarks>
internal static class SaveCommand
{
    /// <summary>The extensions it writes, as the complaint about any other names them.</summary>
    public static string Formats =>
        $".{PatchIO.FileExtension}, .{PatchLanguage.FileExtension} or {PatchBundle.Extension}";

    /// <summary>Saves a patch read from <paramref name="file"/>, whose files are beside it.</summary>
    public static int Run(FileInfo file, FileInfo output, ModuleCatalog modules, TextWriter error, TextWriter writer)
    {
        if (string.Equals(file.FullName, output.FullName, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {output.Name}: this is the file being read.");
            return Exit.Failed;
        }

        if (PatchFile.Bundled(output)) return PackCommand.Run(file, output, error, writer);

        return Patches.Open(file, error) is not { } opened
            ? Exit.Failed
            : Written(opened.Patch, output, modules, error, writer, file.Name, carried: null);
    }

    /// <summary>Saves a preset, whose files are handed over in <paramref name="carried"/>.</summary>
    public static int Run(
        Patch patch,
        string name,
        IReadOnlyDictionary<string, byte[]>? carried,
        FileInfo output,
        ModuleCatalog modules,
        TextWriter error,
        TextWriter writer)
    {
        if (PatchFile.Bundled(output))
            return PackCommand.Run(patch, path => carried?.GetValueOrDefault(path), output, error, writer);

        return Written(patch, output, modules, error, writer, name, carried);
    }

    private static int Written(
        Patch patch,
        FileInfo output,
        ModuleCatalog modules,
        TextWriter error,
        TextWriter writer,
        string name,
        IReadOnlyDictionary<string, byte[]>? carried)
    {
        int code;

        if (PatchFile.Sourced(output))
        {
            code = PrintCommand.Run(patch, null, output, check: false, writer, error, name: name);
        }
        else if (string.Equals(output.Extension, $".{PatchIO.FileExtension}", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.WriteAllText(output.FullName, PatchIO.ToJson(patch, modules));
                code = Exit.Ok;
            }
            catch (Exception ex)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {output.Name}: {ex.Message}");
                return Exit.Failed;
            }
        }
        else
        {
            error.WriteLine(
                $"{GlobalConstants.ApplicationName}: {output.Name}: the extension says what to write, {Formats}.");
            return Exit.Failed;
        }

        if (code == Exit.Failed || carried is not { Count: > 0 }) return code;

        error.WriteLine(
            $"{GlobalConstants.ApplicationName}: {name} plays {Writing.Count(carried.Count, "file")} it carries, "
            + $"which {output.Name} names but cannot hold; save it as {PatchBundle.Extension} to take them along.");

        foreach (var path in carried.Keys) error.WriteLine($"    {path}");

        return Exit.Problems;
    }
}
