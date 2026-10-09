using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>The inspector's rows for how the computer keyboard is laid out, on a MIDI In that listens to it.</summary>
internal sealed class KeyboardSection(NodeEditor editor, TextWriteBack writeBack, InspectorRows rows)
{
    /// <summary>
    /// How the computer keyboard is laid out, on a MIDI In that listens to it.
    /// </summary>
    /// <remarks>
    /// The patch's setting rather than the module's (ADR-0099), shown here
    /// because this is where somebody playing the keys is looking. Every MIDI In
    /// on the keyboard shows the same one, and the heading says so, so that
    /// changing it on one and finding it changed on another is what was
    /// expected. Not shown on a module listening to a device, whose notes are
    /// its own.
    /// </remarks>
    public Control? Build(NodeInstance node)
    {
        if (node.TypeId != NodeCatalog.MidiTypeId) return null;

        var device = new ExtraState(new MidiExtra().Fields, node.StateOf(MidiExtra.StateKey)).Chosen(MidiExtra.DeviceField);

        if (!string.IsNullOrWhiteSpace(device) && device != MidiSources.Keyboard) return null;

        var section = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

        section.Children.Add(new TextBlock
        {
            Text = "computer keyboard — the whole patch's, the same on every MIDI In",
            FontSize = Text.Micro,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var layout = new ExtraField.Choice(
            "layout",
            "layout",
            [new ChoiceOption(Piano, "Piano"), new ChoiceOption(ByScale, "Scale")],
            Piano);

        section.Children.Add(rows.ChoiceRow(
            layout,
            editor.History.Patch.Keyboard is null ? Piano : ByScale,
            picked =>
            {
                // A scale left behind is picked up again, so trying the piano
                // for a moment does not cost the scale that had been chosen.
                if (picked == ByScale) editor.History.Patch.Keyboard = keptKeyboard ?? KeyboardScale.Major;
                else
                {
                    keptKeyboard = editor.History.Patch.Keyboard;
                    editor.History.Patch.Keyboard = null;
                }

                writeBack.Relaid();

                // After the picker has finished with its own event, since what
                // is rebuilt includes the picker.
                Dispatcher.UIThread.Post(() => editor.Reactions.Raise(new PanelStale()));
            }));

        if (editor.History.Patch.Keyboard is not { } scale) return section;

        section.Children.Add(InspectorRows.Helped(
            rows.ChoiceRow(
                new ExtraField.Choice("tonic", "tonic", [.. Tonics.Select(name => new ChoiceOption(name, name))], Tonics[0]),
                Tonics[scale.TonicClass],
                picked => Lay(editor.History.Patch.Keyboard! with { Tonic = Array.IndexOf(Tonics, picked) })),
            "The note each row starts on, at the left."));

        // The scales an Auto Chord builds in, so the two read alike.
        section.Children.Add(InspectorRows.Helped(
            rows.ChoiceRow(
                new ExtraField.Choice("scale", "scale", [.. Chords.Scales.Select(s => new ChoiceOption(s.Id, s.Name))], Chords.Scales[0].Id),
                scale.Mode.Id,
                picked => Lay(editor.History.Patch.Keyboard! with { Scale = picked })),
            "The scale along each row, one note a key."));

        return section;

        void Lay(KeyboardScale next)
        {
            editor.History.Patch.Keyboard = next;
            writeBack.Relaid();
        }
    }

    private const string Piano = "piano";
    private const string ByScale = "scale";

    private static readonly string[] Tonics = [.. Enumerable.Range(0, Pitch.Classes).Select(Pitch.ClassName)];

    /// <summary>The scale last switched away from, for switching back to.</summary>
    private KeyboardScale? keptKeyboard;
}
