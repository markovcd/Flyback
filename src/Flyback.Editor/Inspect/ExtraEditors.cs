using Avalonia.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Editor.Hubs;
using Flyback.Editor.Settings;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The controls that edit what a module carries besides its knobs, for the inspector
/// and for the card over the live picture alike.
/// </summary>
/// <remarks>
/// The one place the App knows the kinds apart, and a lookup here rather than a
/// method on <see cref="NodeExtra"/> because the engine does not reference Avalonia.
/// A kind with nothing here costs a row and not the panel.
/// </remarks>
internal sealed class ExtraEditors(TextWriteBack writeBack, FileRows files, FieldRows fieldRows)
{
    /// <summary>Whether <paramref name="def"/> carries anything these controls edit.</summary>
    public static bool Has(NodeDef def) => def.Extras.Any(extra => extra is StepsExtra or ScaleExtra or ArrangementExtra or FileExtra || extra.Fields.Count > 0);

    /// <summary>The control for each thing it carries, in the order the module declares them, each with its help.</summary>
    /// <param name="reading">Whether a value row keeps a column for a reading, so its bar lines up with the knobs'.</param>
    public IEnumerable<Control> Of(NodeInstance node, NodeDef def, bool reading)
    {
        foreach (var extra in def.Extras)
            if (For(extra, node, def, reading, large: false) is { } control)
                yield return extra.Fields.Count == 0 ? InspectorRows.Helped(control, extra.Help) : control;
    }

    /// <summary>The control for one thing it carries, sized for a finger where the kind has such a size.</summary>
    public Control? Large(NodeExtra extra, NodeInstance node, NodeDef def) => For(extra, node, def, reading: false, large: true);

    private Control? For(NodeExtra extra, NodeInstance node, NodeDef def, bool reading, bool large) => extra switch
    {
        // A sequencer's tune is a list rather than a row of knobs (ADR-0038),
        // so it is edited as one — added to, taken from and reordered.
        StepsExtra steps => new StepList(
            node, steps.Spec, Colors.Palette(def).Accent, because => writeBack.Edited(node, because)).View,

        // A quantiser's scale is a set rather than a sequence, so it is edited
        // as the octave it is a subset of rather than as a list of numbers.
        ScaleExtra => new ScaleKeys(node, def, because => writeBack.Edited(node, because), large).View,

        // An Arrangement's parts are a grid, read as a shape and written a row at a time.
        ArrangementExtra => new PartGrid(node, def, because => writeBack.Edited(node, because)).View,

        // The one a node carries that is not a number, so it is a name and a
        // button rather than a control with a range.
        FileExtra file => files.Row(node, file),

        // Anything else is a plugin's own kind, which ships no control and is
        // drawn from what it declares instead — see ADR-0055. A kind that
        // declares nothing simply gets no rows.
        _ => fieldRows.Declared(node, extra, reading, large),
    };
}
