using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Editor.Canvas;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Ui.Midi;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The inspector's rows for what a plugin's module carries, drawn from the
/// fields it declares (ADR-0055).
/// </summary>
/// <param name="instruments">The instruments a MIDI In can be played from, whose tracks name its channels.</param>
internal sealed class FieldRows(NodeEditor editor, TextWriteBack writeBack, MidiHub midi, InstrumentLibrary instruments, InspectorRows rows)
{
    /// <summary>
    /// A plugin's extra, drawn from its <see cref="NodeExtra.Fields"/>.
    /// </summary>
    /// <remarks>
    /// Knowledge of the vocabulary rather than of any plugin: nothing here could
    /// tell you which one it is drawing. A field shape this build has never heard
    /// of is skipped rather than drawn wrongly.
    /// </remarks>
    public Control? Declared(NodeInstance node, NodeExtra extra, bool reading)
    {
        if (extra.Fields.Count == 0) return null;

        var section = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        section.Children.Add(new TextBlock
        {
            Text = extra.Key,
            FontSize = Text.Micro,
            Foreground = Text.Muted,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var field in extra.Fields)
            if (FieldRow(node, extra, field, reading) is { } control)
                section.Children.Add(InspectorRows.Helped(control, field.Help));

        return section;
    }

    private Control? FieldRow(NodeInstance node, NodeExtra extra, ExtraField field, bool reading) => field switch
    {
        // A MIDI In's channel is a list of tracks where its instrument is known by name.
        ExtraField.Number number when extra is MidiExtra && field.Key == MidiExtra.ChannelField
            && TracksOf(node) is { } tracks => rows.ChoiceRow(
                new ExtraField.Choice(field.Key, field.Label, tracks, "0"),
                ((int)number.Value(node.StateOf(extra.Key)?[field.Key])).ToString(System.Globalization.CultureInfo.InvariantCulture),
                next => Store(node, extra, field, JsonValue.Create(float.TryParse(next, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var channel) ? channel : 0f))),

        ExtraField.Number number => rows.ValueRow(
            field.Label,
            number.Spec,
            number.Value(node.StateOf(extra.Key)?[field.Key]),
            $"{node.Id} {extra.Key} {field.Key}",
            next => Store(node, extra, field, JsonValue.Create(next)),
            reading),

        ExtraField.Toggle toggle => rows.ToggleRow(
            field.Label,
            toggle.Value(node.StateOf(extra.Key)?[field.Key]),
            next => Store(node, extra, field, JsonValue.Create(next))),

        ExtraField.Choice choice => rows.ChoiceRow(
            choice,
            choice.Value(node.StateOf(extra.Key)?[field.Key]),
            next =>
            {
                Store(node, extra, field, JsonValue.Create(next));

                // The keyboard's section belongs to a MIDI In on the keyboard,
                // and the channel row's shape to its instrument, so both come and
                // go with the device.
                if (extra is MidiExtra && field.Key == MidiExtra.DeviceField) Dispatcher.UIThread.Post(() => editor.Reactions.Raise(new PanelStale()));
            },

            // What the same field would say if asked again. An extra is free to
            // compute its fields afresh — MidiExtra does, because what it lists
            // is what is plugged in — and this is how the list gets a second
            // chance to be right without the panel being rebuilt.
            () => extra.Fields
                .OfType<ExtraField.Choice>()
                .FirstOrDefault(again => again.Key == field.Key)?.Options ?? choice.Options),

        ExtraField.Text text => rows.TextRow(
            text,
            text.Value(node.StateOf(extra.Key)?[field.Key]),
            next =>
            {
                if (node.TypeId != NodeCatalog.SendTypeId)
                {
                    Store(node, extra, field, JsonValue.Create(next));
                    return;
                }

                // A Send's only text is its bus, and its Receives go where it goes.
                foreach (var receive in BusEdits.Rename(editor.History.Patch, node, next)) writeBack.Restated(receive.Id, field.Key);

                writeBack.Restated(node.Id, field.Key);
            },

            // An Expression's formula is the one field whose text is a language,
            // so it is the one that can be marked as unread.
            node.TypeId == NodeCatalog.ExpressionTypeId ? NodeCatalog.FormulaProblem : null),

        _ => null,
    };

    /// <summary>
    /// Writes one field of a plugin's extra back, making the stored object first
    /// where the module arrived without one.
    /// </summary>
    /// <remarks>
    /// Through the field's own tidying, which is where an extra's range differs
    /// from a knob's: a socket's <see cref="PortSpec.Min"/> is the editor's
    /// suggestion and a saved value outside it widens the slider, where a field's
    /// range is what the value means.
    /// </remarks>
    private void Store(NodeInstance node, NodeExtra extra, ExtraField field, JsonNode value)
    {
        var held = extra.Stored(node.StateOf(extra.Key));
        held[field.Key] = field.Sane(value);

        node.SetState(extra.Key, held);

        // Noted rather than written, for the reason a knob is: a field on a
        // slider is dragged, and the text should be edited once at the end of it.
        writeBack.Restated(node.Id, field.Key);
    }

    /// <summary>
    /// The channels a MIDI In may listen to, as the tracks of the instrument it
    /// is listening to, or null where that instrument is not one Flyback knows.
    /// </summary>
    private IReadOnlyList<ChoiceOption>? TracksOf(NodeInstance node)
    {
        var device = new ExtraState(new MidiExtra().Fields, node.StateOf(MidiExtra.StateKey)).Chosen(MidiExtra.DeviceField);
        var source = midi.Sources.FirstOrDefault(s => s.Id == device);

        if (source.Id is null || instruments.For(source) is not { Tracks.Count: > 0 } profile) return null;

        return
        [
            new ChoiceOption("0", "Every channel"),
            .. profile.Tracks.Select(track => new ChoiceOption(
                track.Channel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{track.Name} · channel {track.Channel}")),
        ];
    }
}
