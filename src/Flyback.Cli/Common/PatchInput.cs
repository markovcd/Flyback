using System.CommandLine;
using Flyback.Core;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Common;

/// <summary>
/// What a command reads its patch from: a file, or <c>--preset</c> and a shipped
/// preset's name, with <c>--presets</c> to list the names.
/// </summary>
internal sealed class PatchInput
{
    /// <summary>What the patch argument says when a command does not say otherwise.</summary>
    public const string PatchDescription = "The patch to read: a document, a bundle, or one written as text. "
        + "The extension decides which — ." + PatchIO.FileExtension + ", "
        + PatchBundle.Extension + " or ." + PatchLanguage.FileExtension + ". Left out, give --preset instead.";

    private readonly string verb;

    /// <param name="verb">What the command does to the patch, as in "say what to <i>render</i>".</param>
    /// <param name="patch">The patch argument's description.</param>
    /// <param name="preset">The <c>--preset</c> option's description.</param>
    public PatchInput(
        string verb,
        string patch = PatchDescription,
        string preset = "A shipped preset, by name, in place of a file.")
    {
        this.verb = verb;

        Patch = new Argument<FileInfo?>("patch") { Description = patch, Arity = ArgumentArity.ZeroOrOne };
        Preset = new Option<string>("--preset") { Description = preset };
        Presets = new Option<bool>("--presets") { Description = "List what --preset would accept, and stop." };
    }

    public Argument<FileInfo?> Patch { get; }

    public Option<string> Preset { get; }

    public Option<bool> Presets { get; }

    /// <summary>Lists the presets when <c>--presets</c> asks, and says whether it did.</summary>
    public bool Listed(ParseResult result, PluginRegistry plugins, bool json = false)
    {
        if (!result.GetValue(Presets)) return false;

        ShippedPresets.List(plugins.Catalog, result.InvocationConfiguration.Output, json);

        return true;
    }

    /// <summary>
    /// The file or the preset's name, exactly one of them; false, with the reason
    /// written to the command's error, where neither or both were given.
    /// </summary>
    public bool Chosen(ParseResult result, out FileInfo? file, out string? preset)
    {
        file = result.GetValue(Patch);
        preset = result.GetValue(Preset);

        if ((file is null) != (preset is null)) return true;

        result.InvocationConfiguration.Error.WriteLine(
            $"{GlobalConstants.ApplicationName}: say what to {verb}: a patch, or --preset and its name.");

        return false;
    }
}
