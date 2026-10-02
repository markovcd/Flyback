using Avalonia.Input.Platform;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Editor.Statistics;

namespace Flyback.Editor.Canvas;

/// <summary>
/// Copy, cut and paste of modules, through the system clipboard as the JSON a patch
/// is saved as (ADR-0045), so what is copied pastes into another Flyback and into a
/// text editor.
/// </summary>
/// <remarks>
/// Each takes the clipboard it is to use: the canvas hands over its window's, and a
/// test can hand over one of its own. What goes wrong is returned to be said, since a
/// clipboard is the one place the canvas reaches outside itself.
/// </remarks>
internal sealed class CanvasClipboard(CanvasHistory history, CanvasSelection selection, CanvasEdits edits, Usage usage)
{
    /// <summary>
    /// Puts the selected modules on the clipboard. Nothing happens where the selection
    /// holds nothing copiable, rather than the clipboard being emptied by a gesture
    /// that found nothing.
    /// </summary>
    /// <returns>What to say about it, or null where there is nothing to say.</returns>
    public async Task<string?> CopyAsync(IClipboard? clipboard)
    {
        if (selection.Count == 0 || clipboard is null) return null;

        var fragment = PatchClipboard.Copy(history.Patch, selection.Ids);

        // Selected, and yet none of it can be copied, which can only be the Output.
        if (fragment.Nodes.Count == 0) return "The Output cannot be copied.";

        await clipboard.SetTextAsync(PatchIO.ToJson(fragment, NodeCatalog.Current));
        usage.Count(Used.Copied);
        return null;
    }

    /// <summary>Copies the selection and then deletes it; a copy that fails deletes nothing.</summary>
    public async Task<string?> CutAsync(IClipboard? clipboard)
    {
        var trouble = await CopyAsync(clipboard);
        if (trouble is null && selection.Count > 0) edits.DeleteSelected();

        return trouble;
    }

    /// <summary>
    /// Reads a patch off the clipboard, as a patch file or as patch text, and merges
    /// it in, centered on the view and left selected, as one edit. A fragment naming
    /// a module this build has not got is refused with the sentence
    /// <see cref="PatchLoad.Summary"/> already words.
    /// </summary>
    /// <returns>What to say about it, or null where there is nothing to say.</returns>
    public async Task<string?> PasteAsync(IClipboard? clipboard)
    {
        if (clipboard is null) return null;

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;

        Patch fragment;

        try
        {
            var loaded = PatchIO.Read(text, NodeCatalog.Current);

            if (!loaded.IsComplete) return $"Not pasted. {loaded.Summary}";

            fragment = loaded.Patch;
        }
        catch (Exception)
        {
            if (Built(text) is not { } built)
                return "Nothing to paste: the clipboard holds neither a patch nor text that builds one.";

            fragment = built;
        }

        edits.AddFragment(fragment);
        usage.Count(Used.Pasted);
        return null;
    }

    /// <summary>
    /// The modules and groups patch text builds, or null where it does not build or
    /// builds none. What the text says about the whole patch stays behind.
    /// </summary>
    private static Patch? Built(string text)
    {
        var load = PatchLanguage.Build(text);

        if (!load.Ok) return null;

        var bare = PatchClipboard.Bare(load.Patch);

        return bare.Nodes.Count == 0 ? null : bare;
    }
}
