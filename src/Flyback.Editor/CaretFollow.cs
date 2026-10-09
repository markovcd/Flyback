using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Engine.Language;

namespace Flyback.Editor;

/// <summary>
/// Points the inspector at the module the text's caret is standing in, the same
/// selection the canvas points.
/// </summary>
internal sealed class CaretFollow(NodeEditor editor, Reactions reactions)
{
    /// <summary>
    /// Whether the caret is standing on a module the patch on the canvas has moved
    /// on from, so the panel has nothing honest to show for it. Read by the panel,
    /// which says so rather than sitting empty.
    /// </summary>
    public bool IsAdrift { get; private set; }

    /// <summary>Whether what the caret stands on and the patch has moved on from is a group rather than a module.</summary>
    public bool IsAdriftBox { get; private set; }

    /// <summary>
    /// Whether the text is being written to from the panel rather than typed into.
    /// </summary>
    /// <remarks>
    /// Replacing a knob's number moves the caret along and the editor reports that
    /// as a caret move, which it is not. The selection must not follow it, or
    /// letting go of a slider would empty the panel that slider is in.
    /// </remarks>
    public bool Held { get; private set; }

    /// <summary>Holds the selection where it is until what this returns is disposed.</summary>
    public IDisposable Hold()
    {
        Held = true;

        return new Release(this);
    }

    /// <summary>
    /// Points the inspector at what the caret at <paramref name="at"/> stands in.
    /// </summary>
    /// <remarks>
    /// A caret on a word that names no module selects none — the space between two
    /// statements is not a module — and so does a word the patch has moved on
    /// from, see <see cref="Adrift"/>.
    /// </remarks>
    /// <param name="map">Where the text says each module and group.</param>
    /// <param name="means">The patch the text builds, or null where the text is a printing of the canvas.</param>
    public void Follow(int at, SourceMap map, Patch? means)
    {
        // A group's header, its closing brace, or anywhere in its block that is
        // about no module: the group, which the panel shows as the canvas does
        // for a box. A group the canvas has not got yet is one the text has
        // moved on to, and says so the way a module does.
        if (map.GroupAt(at) is { } boxed)
        {
            var group = editor.History.Patch.Groups?.FirstOrDefault(g => g.Id == boxed);
            var shifted = (group is null) != IsAdrift || (group is null && !IsAdriftBox);

            IsAdrift = group is null;
            IsAdriftBox = group is null;

            if (group is null) editor.Selection.Select(null);
            else editor.Selection.SelectGroup(group);

            if (shifted && group is null) reactions.Raise(new PanelStale());

            return;
        }

        var named = map.At(at);
        var lost = named is { } id && Adrift(id, means);

        // Before the selection, because changing it is what rebuilds the panel
        // and the panel reads this on the way past.
        var moved = lost != IsAdrift || (lost && IsAdriftBox);

        IsAdrift = lost;
        IsAdriftBox = false;

        editor.Selection.Select(lost ? null : named);

        // And where the selection did not change — a caret moving between two
        // words the patch has both moved on from — the panel is asked again
        // anyway, since what it has to say has changed even though what is
        // selected has not.
        if (moved && editor.Selection.Focused is null) reactions.Raise(new PanelStale());
    }

    /// <summary>
    /// Whether the module the text means by <paramref name="id"/> is not the one
    /// the canvas has under that name.
    /// </summary>
    /// <remarks>
    /// The binder names a module after where it stands, so a module typed in ahead
    /// of another renames that other one, and between an edit and the apply that
    /// answers for it the names in the text are not the names on the canvas. A
    /// name that is there and means something else would point the panel at the
    /// wrong module, quietly. The type is what is compared, which misses only a
    /// module swapped for another of its kind — where the knobs are the same row.
    /// </remarks>
    private bool Adrift(Guid id, Patch? means) =>
        means is { } text && text.Find(id)?.TypeId != editor.History.Patch.Find(id)?.TypeId;

    private sealed class Release(CaretFollow caret) : IDisposable
    {
        public void Dispose() => caret.Held = false;
    }
}
