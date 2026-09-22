using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.App.Controls;

/// <summary>The rows the inspector and the settings window are made of: a name in a shared gutter and the control beside it.</summary>
/// <param name="changed">Tells the canvas the patch changed, filed under the gesture named, if any.</param>
/// <param name="handOff">Says the hand has come off a control, which is when an edit reaches the text.</param>
internal sealed class InspectorRows(Action<string?> changed, Action handOff)
{
    /// <summary>
    /// How wide the column every row puts its name in is. One number rather than
    /// seven, because it is stated twice per row — as the grid column and as the
    /// caption's own width, so a name too long to fit is trimmed at the gutter
    /// rather than pushing the control along.
    /// </summary>
    internal const double Gutter = 78;

    /// <summary>
    /// The gutter of a row in the settings window, which is wider than a knob's:
    /// its names are whole phrases like "Startup patch", and the window has the
    /// room the inspector does not.
    /// </summary>
    internal const double SettingsGutter = SettingsForm.Gutter;

    /// <summary>A row's name, in the gutter every row shares.</summary>
    internal static TextBlock Caption(string text, double width = Gutter) => new()
    {
        Text = text,
        Width = width,
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>The gutter, and whatever columns the caller needs beside it.</summary>
    internal static Grid Row(string beside, double gutter = Gutter) =>
        new() { ColumnDefinitions = new ColumnDefinitions($"{gutter},{beside}") };

    /// <summary>
    /// A knob whose number is not what it means, and so wants a column for what it
    /// does mean: "57" is not what anyone means by the note they are picking. A
    /// count needs no such column, but lands on whole numbers for the same reason a
    /// note does.
    /// </summary>
    private static bool Named(PortSpec spec) => spec.Display != PortDisplay.Number;

    /// <summary>Whether any socket or field on this module has a reading to show.</summary>
    /// <remarks>
    /// Checked once for a module rather than once per row, because the answer
    /// governs a column every row on the panel shares — see <see cref="KnobRow"/>.
    /// </remarks>
    internal static bool ShowsReading(NodeDef def) =>
        def.Inputs.Any(Named) ||
        def.Extras.Any(extra => extra.Fields
            .Any(field => field is ExtraField.Number number && Named(number.Spec)));

    /// <summary>A knob's row: the slider, the reading if it has one, and its number.</summary>
    /// <remarks>
    /// <paramref name="reading"/> reserves the column for the whole panel rather
    /// than this row, so every slider on a module is the same width and no row
    /// reserves it when nothing on the module has a reading. 60 fits the widest
    /// there is: a duration just under a second, as "999.9 ms".
    /// </remarks>
    internal static Grid KnobRow(bool reading) => Row(reading ? "*,60,84" : "*,84");

    /// <summary>A labeled row in the settings window, on the gutter its declared rows use too.</summary>
    internal static Control Field(string name, Control control)
    {
        var row = Row("*", SettingsGutter);

        var label = Caption(name, SettingsGutter);

        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);

        row.Children.Add(label);
        row.Children.Add(control);

        return row;
    }

    /// <summary>
    /// A label and a box to type into, on the same grid a knob's row uses —
    /// several lines tall where the field takes several lines.
    /// </summary>
    /// <remarks>
    /// Kept when Enter is pressed or the focus goes elsewhere, and put back by
    /// Escape, as a name being typed is. Stored once, when it is kept, rather
    /// than at every key: a formula half typed is not one anybody meant, and
    /// each would be a step in the history. In a box of several lines Enter
    /// starts the next one, so there it is Ctrl+Enter that keeps it.
    /// </remarks>
    /// <param name="problem">
    /// What stops the module reading a value, where the module reads one. It is
    /// asked about what was kept rather than about what is being typed, since
    /// half a formula does not read and nobody meant it yet.
    /// </param>
    internal Control TextRow(
        ExtraField.Text field,
        string value,
        Action<string> store,
        Func<string, string?>? problem = null)
    {
        var row = Row("*");

        var caption = Caption(field.Label);
        caption.VerticalAlignment = field.Multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        caption.Margin = field.Multiline ? new Thickness(0, 6, 0, 0) : default;

        var box = new TextBox
        {
            Text = value,
            MaxLength = ExtraField.Text.Limit,
            FontSize = Text.Body,
            AcceptsReturn = field.Multiline,
            TextWrapping = field.Multiline ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MinHeight = field.Multiline ? 72 : 0,
            VerticalAlignment = VerticalAlignment.Center,
        };

        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when field.Multiline && !e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    return;

                case Key.Enter:
                    Keep();
                    break;

                case Key.Escape:
                    box.Text = value;
                    break;

                default:
                    return;
            }

            e.Handled = true;
        };

        box.LostFocus += (_, _) => Keep();

        Mark();

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(caption);
        row.Children.Add(box);

        return row;

        void Keep()
        {
            // The box breaks lines the way the platform does, and what is kept
            // breaks them one way — see ExtraField.Text — so leaving it untouched
            // is not an edit.
            var typed = (box.Text ?? string.Empty).ReplaceLineEndings("\n");
            if (typed == value) return;

            value = typed;
            store(typed);
            Mark();
            changed(null);

            // Kept is the hand coming off, and the only sign of it there will be:
            // the panel is drawn again with what was typed, so the key that did it
            // is let go of over a box that is no longer there.
            handOff();
        }

        // The mark is on what the module is computing, not on what is under the
        // caret, so it stays while an unread value is being corrected.
        void Mark()
        {
            var said = problem?.Invoke(value);

            if (said is null)
            {
                box.ClearValue(TemplatedControl.ForegroundProperty);
                box.ClearValue(TemplatedControl.BorderBrushProperty);
                ToolTip.SetTip(box, null);
                return;
            }

            box.Foreground = Unread;
            box.BorderBrush = Unread;
            ToolTip.SetTip(box, $"This does not read: {said}. It gives 0 until it does.");
        }
    }

    /// <summary>What a value the module cannot read is marked in.</summary>
    private static readonly IBrush Unread = new SolidColorBrush(Colors.Sink);

    /// <summary>
    /// A label and a list to pick from, on the same grid a knob's row uses.
    /// </summary>
    /// <remarks>
    /// What is stored may not be in the list — a device switched off, a patch
    /// written on another machine — and that is shown rather than corrected: an
    /// entry for it is added at the end, so the picker shows what the patch means.
    /// <paramref name="fresh"/> is asked again as the list opens, which is the one
    /// moment it matters; a list that changed under a pointer already inside it
    /// would move the row somebody was reaching for.
    /// </remarks>
    internal Control ChoiceRow(
        ExtraField.Choice choice,
        string value,
        Action<string> store,
        Func<IReadOnlyList<ChoiceOption>>? fresh = null)
    {
        var row = Row("*");

        var caption = Caption(choice.Label);

        // What is stored is always in the list, whether or not it is here.
        List<ChoiceOption> Offer(IReadOnlyList<ChoiceOption> from)
        {
            var offered = from.ToList();

            if (offered.All(option => option.Id != value))
                offered.Add(new ChoiceOption(value, choice.Name(value)));

            return offered;
        }

        var options = Offer(choice.Options);

        var list = new Picker
        {
            ItemsSource = options,
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ChoiceOption.Name)),
            SelectedIndex = options.FindIndex(option => option.Id == value),
            FontSize = Text.Body,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ChoiceOption picked || picked.Id == value) return;

            // Discrete, so it names no gesture: picking again is picking again
            // rather than one pick going on. Named, two of them would fold into
            // one step — and a pick that came back to where it started would
            // leave a step that puts nothing back.
            value = picked.Id;
            store(picked.Id);
            changed(null);
        };

        if (fresh is not null)
            list.DropDownOpened += (_, _) =>
            {
                var offered = Offer(fresh());

                // Left alone when nothing has changed, which is nearly always.
                // Replacing the items clears the selection on the way past, and
                // doing that for no reason is how a picker loses its place.
                if (offered.Select(option => option.Id).SequenceEqual(options.Select(option => option.Id))) return;

                options = offered;

                list.ItemsSource = options;
                list.SelectedIndex = options.FindIndex(option => option.Id == value);
            };

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(list, 1);
        row.Children.Add(caption);
        row.Children.Add(list);

        return row;
    }

    /// <summary>A label and a switch, laid out on the same grid a knob's row uses.</summary>
    internal Control ToggleRow(string label, bool value, Action<bool> store)
    {
        var row = Row("*");

        var caption = Caption(label);

        var box = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };

        box.IsCheckedChanged += (_, _) =>
        {
            // Discrete, for the reason a picker is: on and off again are two
            // things done rather than one held down, and folding them would
            // leave a step whose patch is the one already showing.
            store(box.IsChecked == true);
            changed(null);
        };

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(box, 1);
        row.Children.Add(caption);
        row.Children.Add(box);

        return row;
    }

    /// <summary>
    /// A label, a slider, a number box, and — where the number is not what it means
    /// — what it does mean written beside them.
    /// </summary>
    /// <remarks>
    /// Shared by a socket's knob and by a plugin's declared number field, so a
    /// plugin gets snapping, formatting and the widened range for nothing. The
    /// caller says where the value lives; nothing here knows whether that is an
    /// input array or a stored object.
    /// </remarks>
    /// <param name="label">What to write in the left column.</param>
    /// <param name="spec">The range, the display and whether it snaps.</param>
    /// <param name="value">What it starts at.</param>
    /// <param name="because">
    /// What to file the edit under, so dragging is one undo step rather than one
    /// per frame.
    /// </param>
    /// <param name="store">Where the new value goes.</param>
    /// <param name="reading">
    /// Whether the panel reserves a column for a reading at all — see
    /// <see cref="ShowsReading"/>. A row whose socket has nothing to say there
    /// still gets the column when a neighbor needs it.
    /// </param>
    /// <param name="reads">What the value means where the socket's own display cannot say, as an Auto remap's fraction does.</param>
    /// <param name="flag">Why the number box is outlined, or null to leave it plain.</param>
    internal Control ValueRow(
        string label,
        PortSpec spec,
        float value,
        string because,
        Action<float> store,
        bool reading,
        Func<float, string>? reads = null,
        string? flag = null)
    {
        var named = Named(spec) || reads is not null;
        var said = reads ?? spec.Format;
        var whole = spec.Stepped;

        var row = KnobRow(reading);

        var caption = Caption(label);

        Grid.SetColumn(caption, 0);
        row.Children.Add(caption);

        // Widen the range if a saved value sits outside the module's usual span.
        var min = Math.Min(spec.Min, value);
        var max = Math.Max(spec.Max, value);

        // A socket with a knee slides in travel rather than in value.
        var tapered = spec.Knee > 0f;

        var slider = new Slider
        {
            Minimum = tapered ? 0 : min,
            Maximum = tapered ? 1 : max,
            Value = Place(value),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),

            // Dragging a note or a count lands on whole numbers. The module
            // quantizes whatever it is given anyway, so a slider that stopped
            // between two would only be showing a distinction the patch does
            // not have.
            IsSnapToTickEnabled = whole,
            TickFrequency = 1,
        };

        var numeric = new NumericUpDown
        {
            Value = Boxed.Of(value),
            Increment = whole ? 1m : 0.05m,
            FormatString = whole ? "0.##" : "0.###",
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
            ShowButtonSpinner = false,
        };

        var name = new TextBlock
        {
            Text = said(value),
            FontSize = Text.Body,
            Opacity = 0.75,
            Margin = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var updating = false;

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && e.NewValue is double d)
                Apply(tapered ? spec.At(d, min, max) : (float)d);
        };

        numeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } d) Apply((float)d);
        };

        Boxed.NeverBlank(numeric);

        if (flag is not null)
        {
            numeric.BorderBrush = new SolidColorBrush(Colors.Attention);
            numeric.BorderThickness = new Thickness(1.5);
            ToolTip.SetTip(numeric, flag);
        }

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(numeric, reading ? 3 : 2);
        row.Children.Add(slider);
        row.Children.Add(numeric);

        if (named)
        {
            Grid.SetColumn(name, 2);
            row.Children.Add(name);
        }

        return row;

        void Apply(float next)
        {
            if (updating) return;

            updating = true;
            store(next);
            slider.Value = Place(next);
            numeric.Value = Boxed.Of(next);
            name.Text = said(next);
            updating = false;

            changed(because);
        }

        double Place(float at) => tapered ? spec.Travel(at, min, max) : at;
    }
}
