using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Plugins.Settings;

namespace Flyback.App.Controls;

/// <summary>
/// A plugin's settings, drawn from what the plugin says it has — an assistant's
/// (ADR-0069) or a sound backend's (ADR-0085).
/// </summary>
/// <remarks>
/// Knowledge of the vocabulary rather than of any plugin: nothing here could
/// tell you which one it is drawing, and no model, endpoint or device appears in it
/// (ADR-0069).
/// <para>
/// The declaration is asked for again after every change, because half a form
/// depends on the rest of it. What comes back is reconciled onto the controls
/// already here rather than replacing them: a control that is rebuilt loses the
/// caret somebody was typing at. A shape this build has never heard of is skipped,
/// so a plugin written against a later vocabulary loses a row rather than the
/// form.
/// </para>
/// </remarks>
public sealed class SettingsForm : UserControl
{
    /// <summary>
    /// How wide the column a caption sits in is, on a form whose captions sit
    /// beside their controls — the same column the settings window's own rows
    /// use, so a declared row lines up with the host's rows under it.
    /// </summary>
    public const double Gutter = 96;

    private readonly StackPanel rows = new() { Spacing = 10 };
    private readonly Dictionary<string, Row> built = new(StringComparer.Ordinal);

    /// <summary>
    /// What the form last stood at, so that a declaration arriving in the same
    /// order does not disturb the children — see <see cref="Declare"/>.
    /// </summary>
    private string[] showing = [];

    private Func<SettingValues, IReadOnlyList<SettingField>>? declare;

    /// <summary>
    /// Whether the controls are being written to rather than typed in. A change
    /// made from here is the declaration arriving, not somebody answering it,
    /// and taking it for an answer would have the form ask the plugin about
    /// its own reply for as long as the stack held.
    /// </summary>
    private bool quiet;

    public SettingsForm() => Content = rows;

    /// <summary>
    /// Whether a caption sits beside its control rather than above it. Beside
    /// where the form shares a section with rows laid out that way, as the Sound
    /// section's does; above where it stands alone, as the agent's does.
    /// </summary>
    public bool Beside { get; init; }

    /// <summary>Raised when somebody changes something. Not when the form is filled in.</summary>
    public event EventHandler? Changed;

    /// <summary>Everything on the form as it stands, in the shape the file keeps.</summary>
    public SettingValues Values { get; private set; } = SettingValues.None;

    /// <summary>
    /// Puts a plugin's form up, with what is already set on it.
    /// </summary>
    /// <param name="declaring">
    /// What the plugin says it has, asked afresh on every change. Null for no
    /// plugin at all, which draws nothing — there is nothing to configure
    /// until something is installed.
    /// </param>
    /// <param name="values">What that plugin was last set to.</param>
    public void Show(Func<SettingValues, IReadOnlyList<SettingField>>? declaring, SettingValues values)
    {
        // A different plugin's fields are different fields, whatever they are
        // called: two plugins may both have a "model" and mean quite different
        // lists by it.
        built.Clear();
        rows.Children.Clear();
        showing = [];

        declare = declaring;
        Values = values;

        Declare();
    }

    /// <summary>
    /// Sets one value from outside, as though the plugin had been answered with
    /// it, and asks for the form again.
    /// </summary>
    /// <remarks>
    /// Not an answer, so <see cref="Changed"/> stays quiet: nobody typed it. It is
    /// how something found out about the plugin rather than asked of somebody — a
    /// survey of an endpoint — lands on the form without this having to know what
    /// it is.
    /// </remarks>
    public void Put(string key, string value)
    {
        Values = Values.With(key, value);

        Declare();
    }

    /// <summary>
    /// Asks the plugin what the form should be now, and makes it so.
    /// </summary>
    /// <remarks>
    /// The children are left alone unless the declared keys have actually
    /// changed. Taking a control out of the visual tree and putting it back
    /// costs the caret and the selection inside it, and the common case by far
    /// is a declaration that differs only in what a row says about itself.
    /// </remarks>
    private void Declare()
    {
        var fields = declare?.Invoke(Values) ?? [];
        var order = new List<string>(fields.Count);

        quiet = true;

        try
        {
            foreach (var field in fields)
            {
                if (!built.TryGetValue(field.Key, out var row))
                {
                    if (Build(field) is not { } fresh) continue;

                    fresh.Beside = Beside;

                    built[field.Key] = row = fresh;
                }

                row.Apply(field, Values);
                order.Add(field.Key);
            }
        }
        finally
        {
            quiet = false;
        }

        if (order.Count == showing.Length && order.SequenceEqual(showing)) return;

        showing = [.. order];
        rows.Children.Clear();

        foreach (var key in showing)
            rows.Children.Add(built[key].View);
    }

    /// <summary>Takes somebody's answer, and asks the plugin what that makes of the rest.</summary>
    private void Answered(string key, string value)
    {
        if (quiet) return;

        Values = Values.With(key, value);

        Declare();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Row? Build(SettingField field) => field switch
    {
        SettingField.Text text => new TextRow(text, Answered),
        SettingField.Pick pick => new PickRow(pick, Answered),
        SettingField.Switch toggle => new SwitchRow(toggle, Answered),
        _ => null,
    };

    private static TextBlock Note() => new()
    {
        FontSize = Text.Small,
        Foreground = Text.Muted,
        Width = 260,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,

        // Left rather than stretched, which every fixed-width thing here has to
        // say: a row is as wide as the caption above it, and a narrower child
        // in it is centered otherwise — which reads as a stray indent.
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// One declared setting, and the control it is answered with.
    /// </summary>
    /// <remarks>
    /// Kept across declarations, which is what lets a field disappear and come
    /// back with the caret and the selection it had. The label is applied every
    /// time along with everything else: a plugin is free to reword one, and
    /// nothing here should have to know whether it did.
    /// </remarks>
    private abstract class Row
    {
        private readonly TextBlock? caption;
        private readonly TextBlock note = Note();

        private bool laid;

        protected Row(string? label) => caption = label is null ? null : Text.Quiet(label);

        public StackPanel View { get; } = new() { Spacing = 3 };

        /// <summary>Whether the caption goes beside the control — see <see cref="SettingsForm.Beside"/>.</summary>
        public bool Beside { get; set; }

        public void Apply(SettingField field, SettingValues values)
        {
            // Laid out on the first application rather than in the constructor,
            // because the control belongs to the subclass and does not exist
            // until its own constructor has run.
            if (!laid)
            {
                laid = true;
                Lay();
            }

            if (caption is not null) caption.Text = field.Label;

            Control.IsEnabled = field.Enabled;
            ToolTip.SetTip(Control, field.Enabled ? null : field.Because);

            note.Text = field.Note ?? string.Empty;
            note.IsVisible = !string.IsNullOrEmpty(field.Note);

            Fill(field, field.Sane(values.All.GetValueOrDefault(field.Key)));
        }

        private void Lay()
        {
            if (!Beside || caption is null)
            {
                if (caption is not null) View.Children.Add(caption);

                View.Children.Add(Control);
                View.Children.Add(note);

                return;
            }

            // The control takes what the gutter leaves rather than its own width,
            // as the host's rows beside it do, and the note starts under the
            // control rather than under the caption.
            caption.Width = Gutter;
            caption.FontSize = Text.Body;
            caption.ClearValue(TextBlock.ForegroundProperty);
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.TextTrimming = TextTrimming.CharacterEllipsis;

            Control.Width = double.NaN;
            Control.HorizontalAlignment = HorizontalAlignment.Stretch;

            note.Width = double.NaN;
            note.Margin = new Thickness(Gutter, 0, 0, 0);

            var line = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Gutter},*") };

            Grid.SetColumn(Control, 1);

            line.Children.Add(caption);
            line.Children.Add(Control);

            View.Children.Add(line);
            View.Children.Add(note);
        }

        protected abstract Control Control { get; }

        /// <summary>Puts <paramref name="value"/> in the control, without it counting as an answer.</summary>
        protected abstract void Fill(SettingField field, string value);
    }

    private sealed class TextRow : Row
    {
        private readonly TextBox box;

        public TextRow(SettingField.Text field, Action<string, string> answered)
            : base(field.Label)
        {
            box = new TextBox
            {
                FontSize = Text.Body,
                Width = 260,
                HorizontalAlignment = HorizontalAlignment.Left,
                Name = field.Key,
                PlaceholderText = field.Placeholder,
            };

            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty) answered(field.Key, box.Text ?? string.Empty);
            };
        }

        protected override Control Control => box;

        protected override void Fill(SettingField field, string value)
        {
            if (box.Text != value) box.Text = value;
        }
    }

    /// <summary>
    /// A list to choose from, which may also be typed into. What is stored is always
    /// in the list whether or not the plugin offered it — a model released after
    /// this build, or one at an endpoint somebody pointed this at by hand — because
    /// a setting that rewrote itself on being looked at is worse than an unfamiliar
    /// name in a box.
    /// </summary>
    private sealed class PickRow : Row
    {
        private readonly ComboBox box;
        private readonly bool editable;

        private List<SettingOption> offered = [];

        public PickRow(SettingField.Pick field, Action<string, string> answered)
            : base(field.Label)
        {
            editable = field.Editable;

            box = new ComboBox
            {
                FontSize = Text.Body,
                Width = 260,
                HorizontalAlignment = HorizontalAlignment.Left,
                Name = field.Key,
                IsEditable = editable,
            };

            if (editable)
            {
                // Typing is what decides an editable one — a name may be picked
                // from the list, typed over it, or arrive from the settings file
                // — so the answer follows the text rather than the selection.
                box.PropertyChanged += (_, e) =>
                {
                    if (e.Property == ComboBox.TextProperty) answered(field.Key, box.Text ?? string.Empty);
                };

                return;
            }

            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex >= 0 && box.SelectedIndex < offered.Count)
                    answered(field.Key, offered[box.SelectedIndex].Id);
            };
        }

        protected override Control Control => box;

        protected override void Fill(SettingField field, string value)
        {
            if (field is not SettingField.Pick pick) return;

            var options = pick.Options.ToList();

            if (options.All(option => option.Id != value) && !string.IsNullOrEmpty(value))
                options.Add(new SettingOption(value, pick.Name(value)));

            // Replacing the items clears the selection on the way past, so it is
            // done only where they have actually changed — otherwise a list
            // loses its place every time anything else on the form moves.
            if (!options.Select(option => option.Id).SequenceEqual(offered.Select(option => option.Id)))
            {
                offered = options;

                // An editable box shows what can be typed, which is the id. A
                // plain one shows the name and stores the id behind it.
                box.ItemsSource = editable
                    ? options.Select(option => option.Id).ToList()
                    : options;

                box.DisplayMemberBinding = editable
                    ? null
                    : new Avalonia.Data.Binding(nameof(SettingOption.Name));
            }

            if (editable)
            {
                if (box.Text != value) box.Text = value;
                return;
            }

            var chosen = offered.FindIndex(option => option.Id == value);

            if (box.SelectedIndex != chosen) box.SelectedIndex = chosen;
        }
    }

    /// <summary>
    /// A switch, whose label is on the box itself rather than above it — there
    /// is nothing to write beside a control that already says what it is.
    /// </summary>
    private sealed class SwitchRow : Row
    {
        private readonly CheckBox box;

        public SwitchRow(SettingField.Switch field, Action<string, string> answered)
            : base(null)
        {
            box = new CheckBox
            {
                Content = field.Label,
                FontSize = Text.Body,
                Name = field.Key,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            box.IsCheckedChanged += (_, _) =>
                answered(field.Key, SettingField.Switch.Spell(box.IsChecked == true));
        }

        protected override Control Control => box;

        protected override void Fill(SettingField field, string value)
        {
            if (field is not SettingField.Switch toggle) return;

            box.Content = toggle.Label;

            var on = toggle.Value(value);

            if (box.IsChecked != on) box.IsChecked = on;
        }
    }
}
