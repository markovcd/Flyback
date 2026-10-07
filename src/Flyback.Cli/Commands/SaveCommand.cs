using System.CommandLine;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using PluginRegistry = Flyback.Cli.Plugins;

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
    /// <summary>
    /// Saves a patch or a shipped preset as a document, a bundle or text, whichever
    /// the output's extension names. It says nothing on success: the file is the answer.
    /// </summary>
    public static Command Build(PluginRegistry plugins)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch to read: a document, a bundle, or one written as text. "
                + $"The extension decides which — .{PatchIO.FileExtension}, "
                + $"{PatchBundle.Extension} or .{PatchLanguage.FileExtension}. Left out, give --preset instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var preset = new Option<string>("--preset")
        {
            Description = "A shipped preset, by name, in place of a file.",
        };

        var presets = new Option<bool>("--presets")
        {
            Description = "List what --preset would accept, and stop.",
        };

        var output = new Option<FileInfo>("--out", "-o")
        {
            Description = $"Where to write it. The extension says what to write: {Formats}.",
        };

        var command = new Command(
            "save",
            "Save a patch or a shipped preset as a patch file, a bundle or text, by the extension it is saved to.")
        {
            patch, preset, presets, output,
        };

        command.SetAction(result =>
        {
            var error = result.InvocationConfiguration.Error;
            var writer = result.InvocationConfiguration.Output;

            plugins.Ready();

            if (result.GetValue(presets))
            {
                ShippedPresets.List(plugins.Catalog, writer);

                return Exit.Ok;
            }

            var file = result.GetValue(patch);
            var named = result.GetValue(preset);

            if ((file is null) == (named is null))
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: say what to save: a patch, or --preset and its name.");

                return Exit.Failed;
            }

            if (result.GetValue(output) is not { } into)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: --out says where to save it, {Formats}.");

                return Exit.Failed;
            }

            if (file is not null) return Run(file, into, plugins.Catalog.Modules, error, writer);

            if (ShippedPresets.Open(plugins.Catalog, named!, error) is not { } shipped) return Exit.Failed;

            return Run(
                shipped.Opened.Patch,
                shipped.Name,
                (shipped.Opened.Samples as BundleFiles)?.Bytes,
                into,
                NodeCatalog.Current,
                error,
                writer);
        });

        return command;
    }

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
