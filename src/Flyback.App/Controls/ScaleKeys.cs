using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The notes a quantiser snaps to, as the octave they are a subset of: twelve
/// switches laid out the way a keyboard lays them out, sharps above naturals.
/// </summary>
/// <remarks>
/// A keyboard rather than a list, which is the opposite call to
/// <see cref="StepList"/>'s and made for the opposite reason. A tune is ordered
/// and may say the same note twice, so it is a list you add to and reorder. A
/// scale is a set of twelve things that are either in or out, and the shape
/// everyone already reads a set of twelve pitches off is an octave of keys —
/// C major is a picture before it is a list of numbers.
/// <para>
/// The layout is the real one and not a row of twelve, because that is the whole
/// of what makes it readable at a glance: the gaps where E–F and B–C meet are
/// how an eye finds which key is which without reading the labels.
/// </para>
/// </remarks>
internal sealed class ScaleKeys
{
    /// <summary>How wide one natural key is. Seven of them fill the panel's width.</summary>
    private const double KeyWidth = 30;

    private const double NaturalHeight = 46;
    private const double SharpHeight = 28;

    /// <summary>Marks the buttons that are keys, so a test can pick them out of the tree.</summary>
    internal const string KeyTag = "scale-key";

    /// <summary>
    /// Which pitch classes are the white keys, in the order they are laid out.
    /// The other five are the black ones, and where each sits is decided by
    /// which natural it follows.
    /// </summary>
    private static readonly int[] Naturals = [0, 2, 4, 5, 7, 9, 11];

    private static readonly IBrush Off = new SolidColorBrush(Colors.Node);
    private static readonly IBrush OnText = Brushes.Black;
    private static readonly IBrush OffText = new SolidColorBrush(Colors.Value);
    private static readonly IPen Edge = new Pen(new SolidColorBrush(Colors.Outline), 1);

    private readonly NodeInstance node;
    private readonly Action<string?> changed;
    private readonly Dictionary<int, Button> keys = [];

    /// <summary>
    /// What a key that is on is painted: the module's own accent, so a lit key
    /// belongs to the block it is on rather than to a palette of its own — and
    /// follows if the Quantiser is ever filed under a different category.
    /// </summary>
    private readonly IBrush on;

    private readonly TextBlock summary = new()
    {
        FontSize = 11,
        Opacity = 0.55,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };

    public ScaleKeys(NodeInstance node, NodeDef def, Action<string?> changed)
    {
        this.node = node;
        this.changed = changed;

        on = new SolidColorBrush(Colors.Accent(def.Category));

        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = "scale",
            FontSize = 9.5,
            Opacity = 0.4,
            Margin = new Thickness(0, 0, 0, 4),
        });

        panel.Children.Add(Keyboard());
        panel.Children.Add(Buttons());
        panel.Children.Add(summary);

        // Fluent gives a button a rounded corner and a hover that lifts it off
        // the panel. A keyboard is a run of keys that touch, so both go: what is
        // wanted here is a shape, and twelve separate buttons would not be one.
        panel.Styles.Add(new Style(x => x.OfType<Button>().Class(KeyTag))
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(2)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(Layoutable.MinWidthProperty, 0d),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
            },
        });

        View = panel;
        Refresh();
    }

    public Control View { get; }

    /// <summary>
    /// The keys, on a canvas because a keyboard is not a stack of anything: a
    /// sharp sits between two naturals and over the join, which is a position
    /// rather than a place in a sequence.
    /// </summary>
    private Control Keyboard()
    {
        var board = new Canvas
        {
            Width = Naturals.Length * KeyWidth,
            Height = NaturalHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        for (var i = 0; i < Naturals.Length; i++)
        {
            var key = Key(Naturals[i], KeyWidth - 1, NaturalHeight);

            Canvas.SetLeft(key, i * KeyWidth);
            Canvas.SetTop(key, 0);
            board.Children.Add(key);
        }

        // Each sharp is the semitone above its natural, and there is one after
        // every natural except E and B — which is exactly the pair that has no
        // black key between them, and is why the gaps are where they are.
        for (var i = 0; i < Naturals.Length; i++)
        {
            var sharp = Naturals[i] + 1;
            if (Naturals.Contains(sharp % Pitch.Classes)) continue;

            var key = Key(sharp, KeyWidth - 9, SharpHeight);

            Canvas.SetLeft(key, ((i + 1) * KeyWidth) - ((KeyWidth - 9) / 2) - 0.5);
            Canvas.SetTop(key, 0);
            board.Children.Add(key);
        }

        return board;
    }

    private Button Key(int pitchClass, double width, double height)
    {
        var key = new Button
        {
            Width = width,
            Height = height,
            FontSize = 9.5,
            Content = new TextBlock
            {
                Text = Pitch.ClassName(pitchClass),
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            },
        };

        key.Classes.Add(KeyTag);
        ToolTip.SetTip(key, $"{Pitch.ClassName(pitchClass)} — every octave of it");

        key.Click += (_, _) => Toggle(pitchClass);

        keys[pitchClass] = key;
        return key;
    }

    /// <summary>
    /// The two scales worth a button. Everything else is a matter of pressing
    /// keys, and a list of modes would be a menu of things this cannot say
    /// afterwards — a scale here has no root, so "D minor" is not a state it
    /// could show you it was in.
    /// </summary>
    private Control Buttons()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Spacing = 6,
        };

        row.Children.Add(Shortcut("All", "Every note, which is the nearest semitone.", [.. Enumerable.Range(0, Pitch.Classes)]));
        row.Children.Add(Shortcut("None", "No note, which passes the signal through unchanged.", []));

        return row;

        Button Shortcut(string label, string tip, int[] scale)
        {
            var button = new Button { Content = label, FontSize = 11 };

            ToolTip.SetTip(button, tip);

            button.Click += (_, _) =>
            {
                ScaleExtra.Set(node, scale);
                Refresh();
                changed(null);
            };

            return button;
        }
    }

    private void Toggle(int pitchClass)
    {
        var scale = ScaleExtra.Of(node);

        if (!scale.Remove(pitchClass)) scale.Add(pitchClass);

        ScaleExtra.Set(node, Pitch.Scale(scale));

        Refresh();
        changed(null);
    }

    /// <summary>
    /// Paints the keys to match the scale and says in words what it adds up to.
    /// </summary>
    /// <remarks>
    /// The line underneath is not decoration. The two ends of the range are the
    /// cases where the module stops being a quantiser — all twelve is the
    /// nearest semitone and none at all is a wire — and both look from the keys
    /// alone like an ordinary scale that happens to be full or empty.
    /// </remarks>
    private void Refresh()
    {
        var scale = ScaleExtra.Of(node);

        foreach (var (pitchClass, key) in keys)
        {
            var lit = scale.Contains(pitchClass);

            key.Background = lit ? on : Off;
            key.BorderBrush = Edge.Brush;
            key.Foreground = lit ? OnText : OffText;
            key.Opacity = lit ? 1 : 0.75;
        }

        summary.Text = scale.Count switch
        {
            0 => "Nothing is switched on, so there is nothing to snap to and the signal "
                 + "passes straight through.",
            Pitch.Classes => "Every note is on, so this snaps to the nearest semitone — "
                             + "which is what a Note module does on its own.",
            _ => $"{scale.Count} notes: {string.Join(" ", scale.Select(Pitch.ClassName))}. "
                 + "Every octave of each, so a sweep runs up the scale.",
        };
    }
}
