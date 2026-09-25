using Avalonia.Input.Platform;
using Flyback.Core.Graph;

namespace Flyback.App.Canvas;

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
internal sealed class CanvasClipboard(CanvasHistory history, CanvasSelection selection, CanvasEdits edits)
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
    /// Reads a patch off the clipboard and merges it in, centered on the view and left
    /// selected, as one edit. A fragment naming a module this build has not got is
    /// refused with the sentence <see cref="PatchLoad.Summary"/> already words.
    /// </summary>
    /// <returns>What to say about it, or null where there is nothing to say.</returns>
    public async Task<string?> PasteAsync(IClipboard? clipboard)
    {
        if (clipboard is null) return null;

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;

        PatchLoad loaded;

        try
        {
            loaded = PatchIO.Read(text, NodeCatalog.Current);
        }
        catch (Exception)
        {
            // Without the parser's wording: the ordinary way here is having copied
            // something else entirely.
            return "Nothing to paste: the clipboard does not hold a patch.";
        }

        if (!loaded.IsComplete) return $"Not pasted. {loaded.Summary}";

        edits.AddFragment(loaded.Patch);
        return null;
    }
}
