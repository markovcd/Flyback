using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;

namespace Flyback.Editor;

/// <summary>A patch file pasted into the text, written as the text its modules and groups would be.</summary>
internal static class PastedPatch
{
    /// <summary>
    /// What <paramref name="pasted"/> writes beside <paramref name="text"/>, empty for
    /// nothing, or null for anything that is not a patch file, which pastes as it is.
    /// </summary>
    /// <remarks>
    /// The names it binds and the groups it opens are ones the text does not
    /// already say, so it builds beside what is there.
    /// </remarks>
    /// <param name="refused">Why nothing was pasted, for a patch file this run cannot read whole.</param>
    public static string? Written(string pasted, string text, out string? refused)
    {
        refused = null;

        if (!pasted.TrimStart().StartsWith('{')) return null;

        PatchLoad loaded;

        try
        {
            loaded = PatchIO.Read(pasted, NodeCatalog.Current);
        }
        catch (Exception)
        {
            return null;
        }

        if (!loaded.IsComplete)
        {
            refused = $"Not pasted. {loaded.Summary}";
            return string.Empty;
        }

        var bare = PatchClipboard.Bare(loaded.Patch);

        return bare.Nodes.Count == 0 ? string.Empty : PatchPrinter.Beside(bare, text);
    }
}
