using Flyback.Editor.Canvas;
using Flyback.Engine.Language;

namespace Flyback.Editor.Hubs;

/// <summary>
/// The groups, def calls and loop passes the document's text opens, while the text owns
/// the patch; none for a patch the canvas owns.
/// </summary>
internal sealed class PatchLevels(Document document, NodeEditor editor)
{
    private string? read;
    private IReadOnlyList<Level> levels = [];

    /// <summary>The levels, outermost first, read again only when the text has changed.</summary>
    public IReadOnlyList<Level> Levels
    {
        get
        {
            if (!document.Owned) return [];

            var text = document.Text;

            if (text == read) return levels;

            var load = PatchLanguage.Build(text);

            // Text that does not read leaves the patch as it was, and so its levels.
            if (load.Ok) levels = load.Levels;

            read = text;

            return levels;
        }
    }

    /// <summary>The levels from the outermost down to the one that placed <paramref name="module"/>; empty for one at the top.</summary>
    public IReadOnlyList<Level> PathTo(Guid module) => Level.PathTo(Levels, module) ?? [];

    /// <summary>The modules of <paramref name="level"/> that are on the canvas.</summary>
    public IReadOnlyList<Guid> Present(Level level) =>
        [.. level.All.Where(id => editor.History.Patch.Find(id) is not null)];
}
