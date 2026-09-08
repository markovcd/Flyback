using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Plugins.Assist;

namespace Flyback.App.Controls;

/// <summary>
/// A provider's settings, drawn from what the provider says it has.
/// </summary>
/// <remarks>
/// <para>
/// The whole of the App's knowledge of an assistant's configuration is here, and
/// it is knowledge of the vocabulary rather than of any provider: nothing in
/// this class could tell you which one it is drawing, and no model name, no
/// endpoint and no capability appears in it. That is ADR-0069, and it is the
/// same route ADR-0055 took for a plugin's carried state.
/// </para>
/// <para>
/// The declaration is asked for again after every change, because half of a form
/// depends on the rest of it — a model decides whether looking is offered at
/// all, a tick decides whether choosing an ear is live. What comes back is
/// reconciled onto the controls already here rather than replacing them: a
/// control that is rebuilt is a control that loses the caret somebody was typing
/// at, and a field that goes and comes back should come back holding what it
/// held.
/// </para>
/// <para>
/// A shape this build has never heard of is skipped rather than drawn wrongly,
/// so a provider written against a later vocabulary loses a row here rather than
/// the form.
/// </para>
/// </remarks>
public sealed class AssistantForm : UserControl
{
    private static readonly IBrush Dim = new SolidColorBrush(Colors.Muted);

    private readonly StackPanel rows = new() { Spacing = 10 };
    private readonly Dictionary<string, Row> built = new(StringComparer.Ordinal);

    /// <summary>
    /// What the form last stood at, so that a declaration arriving in the same
    /// order does not disturb the children — see <see cref="Declare"/>.
    /// </summary>
    private string[] showing = [];

    private Func<AssistantValues, IReadOnlyList<AssistantField>>? declare;

    /// <summary>
    /// Whether the controls are being written to rather than typed in. A change
    /// made from here is the declaration arriving, not somebody answering it,
    /// and taking it for an answer would have the form ask the provider about
    /// its own reply for as long as the stack held.
    /// </summary>
    private bool quiet;

    public AssistantForm() => Content = rows;

    /// <summary>Raised when somebody changes something. Not when the form is filled in.</summary>
    public event EventHandler? Changed;

    /// <summary>Everything on the form as it stands, in the shape the file keeps.</summary>
    public AssistantValues Values { get; private set; } = AssistantValues.None;

    /// <summary>
    /// Puts a provider's form up, with what is already set on it.
    /// </summary>
    /// <param name="declaring">
    /// What the provider says it has, asked afresh on every change. Null for no
    /// provider at all, which draws nothing — there is nothing to configure
    /// until something is installed.
    /// </param>
    /// <param name="values">What that provider was last set to.</param>
    public void Show(Func<AssistantValues, IReadOnlyList<AssistantField>>? declaring, AssistantValues values)
    {
        // A different provider's fields are different fields, whatever they are
        // called: two providers may both have a "model" and mean quite different
        // lists by it.
        built.Clear();
        rows.Children.Clear();
        showing = [];

        declare = declaring;
        Values = values;

        Declare();
    }

    /// <summary>
    /// Asks the provider what the form should be now, and makes it so.
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

    /// <summary>Takes somebody's answer, and asks the provider what that makes of the rest.</summary>
    private void Answered(string key, string value)
    {
        if (quiet) return;

        Values = Values.With(key, value);

        Declare();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Row? Build(AssistantField field) => field switch
    {
        AssistantField.Text text => new TextRow(text, Answered),
        AssistantField.Pick pick => new PickRow(pick, Answered),
        AssistantField.Switch toggle => new SwitchRow(toggle, Answered),
        _ => null,
    };

    private static TextBlock Caption(string text) =>
        new() { Text = text, FontSize = 11, Foreground = Dim };

    private static TextBlock Note() => new()
    {
        FontSize = 11,
        Foreground = Dim,
        Width = 260,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,

        // Left rather than stretched, which every fixed-width thing here has to
        // say: a row is as wide as the caption above it, and a narrower child
        // in it is centred otherwise — which reads as a stray indent.
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// One declared setting, and the control it is answered with.
    /// </summary>
    /// <remarks>
    /// Kept across declarations, which is what lets a field disappear and come
    /// back with the caret and the selection it had. The label is applied every
    /// time along with everything else: a provider is free to reword one, and
    /// nothing here should have to know whether it did.
    /// </remarks>
    private abstract class Row
    {
        private readonly TextBlock? caption;
        private readonly TextBlock note = Note();

        private bool laid;

        protected Row(string? label) => caption = label is null ? null : Caption(label);

        public StackPanel View { get; } = new() { Spacing = 3 };

        public void Apply(AssistantField field, AssistantValues values)
        {
            // Laid out on the first application rather than in the constructor,
            // because the control belongs to the subclass and does not exist
            // until its own constructor has run.
            if (!laid)
            {
                laid = true;

                if (caption is not null) View.Children.Add(caption);

                View.Children.Add(Control);
                View.Children.Add(note);
            }

            if (caption is not null) caption.Text = field.Label;

            Control.IsEnabled = field.Enabled;
            ToolTip.SetTip(Control, field.Enabled ? null : field.Because);

            note.Text = field.Note ?? string.Empty;
            note.IsVisible = !string.IsNullOrEmpty(field.Note);

            Fill(field, field.Sane(values.All.GetValueOrDefault(field.Key)));
        }

        protected abstract Control Control { get; }

        /// <summary>Puts <paramref name="value"/> in the control, without it counting as an answer.</summary>
        protected abstract void Fill(AssistantField field, string value);
    }

    private sealed class TextRow : Row
    {
        private readonly TextBox box;

        public TextRow(AssistantField.Text field, Action<string, string> answered)
            : base(field.Label)
        {
            box = new TextBox
            {
                FontSize = 12,
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

        protected override void Fill(AssistantField field, string value)
        {
            if (box.Text != value) box.Text = value;
        }
    }

    /// <summary>
    /// A list to choose from, which may also be typed into.
    /// </summary>
    /// <remarks>
    /// What is stored is always in the list whether or not the provider offered
    /// it — a model released after this build, or one at an endpoint somebody
    /// pointed this at by hand. It is shown rather than corrected, because a
    /// setting that quietly rewrote itself on being looked at would be worse
    /// than an unfamiliar name in a box.
    /// </remarks>
    private sealed class PickRow : Row
    {
        private readonly ComboBox box;
        private readonly bool editable;

        private List<AssistantOption> offered = [];

        public PickRow(AssistantField.Pick field, Action<string, string> answered)
            : base(field.Label)
        {
            editable = field.Editable;

            box = new ComboBox
            {
                FontSize = 12,
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

        protected override void Fill(AssistantField field, string value)
        {
            if (field is not AssistantField.Pick pick) return;

            var options = pick.Options.ToList();

            if (options.All(option => option.Id != value) && !string.IsNullOrEmpty(value))
                options.Add(new AssistantOption(value, pick.Name(value)));

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
                    : new Avalonia.Data.Binding(nameof(AssistantOption.Name));
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

        public SwitchRow(AssistantField.Switch field, Action<string, string> answered)
            : base(null)
        {
            box = new CheckBox
            {
                Content = field.Label,
                FontSize = 12,
                Name = field.Key,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            box.IsCheckedChanged += (_, _) =>
                answered(field.Key, AssistantField.Switch.Spell(box.IsChecked == true));
        }

        protected override Control Control => box;

        protected override void Fill(AssistantField field, string value)
        {
            if (field is not AssistantField.Switch toggle) return;

            box.Content = toggle.Label;

            var on = toggle.Value(value);

            if (box.IsChecked != on) box.IsChecked = on;
        }
    }
}
