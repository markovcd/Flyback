using Flyback.Editor.Canvas;
using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Editor.Hubs;

/// <summary>
/// The groups, def calls and loop passes the document's text opens, while the text owns
/// the patch; none for a patch the canvas owns.
/// </summary>
internal sealed class PatchLevels
{
    private readonly Document document;
    private readonly NodeEditor editor;

    public PatchLevels(Document document, NodeEditor editor, LevelShelves shelves)
    {
        this.document = document;
        this.editor = editor;

        shelves.Bands = () => Bands;
    }

    private string? read;
    private IReadOnlyList<Level> levels = [];
    private IReadOnlyList<LayoutBand> bands = [];

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
            if (load.Ok)
            {
                levels = load.Levels;
                bands = Level.Bands(levels);
            }

            read = text;

            return levels;
        }
    }

    /// <summary>The levels as the canvas lays them out in bands.</summary>
    private IReadOnlyList<LayoutBand> Bands => Levels.Count == 0 ? [] : bands;

    /// <summary>The levels from the outermost down to the one that placed <paramref name="module"/>; empty for one at the top.</summary>
    public IReadOnlyList<Level> PathTo(Guid module) => Level.PathTo(Levels, module) ?? [];

    /// <summary>The levels from the outermost down to <paramref name="level"/> itself.</summary>
    public IReadOnlyList<Level> PathTo(Level level) => Level.PathTo(Levels, level) ?? [];

    /// <summary>The innermost level of more than one module whose modules are exactly <paramref name="selected"/>, or null.</summary>
    public Level? Matching(IReadOnlyCollection<Guid> selected) =>
        selected.Count < 2
            ? null
            : Level.Every(Levels).LastOrDefault(level => Present(level) is var members && members.Count == selected.Count && members.All(selected.Contains));

    /// <summary>Every call of the def <paramref name="name"/>, in the order the text made them.</summary>
    public IReadOnlyList<Level> CallsOf(string name) =>
        [.. Level.Every(Levels).Where(level => level.Kind == LevelKind.Call && level.Name == name)];

    /// <summary>The modules of <paramref name="level"/> that are on the canvas.</summary>
    public IReadOnlyList<Guid> Present(Level level) =>
        [.. level.All.Where(id => editor.History.Patch.Find(id) is not null)];
}
