using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Capture;
using Flyback.Ui.Controls;
using Flyback.Editor.Notices;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Bars;

/// <summary>
/// The row along the foot of the window that plays the patch: pause, rewind, where the
/// clock is, the seek bar the width of the window, the length, loop, Volume and record.
/// Every target in it is <see cref="Reach"/> square, for a finger as much as a mouse.
/// </summary>
/// <remarks>
/// Hidden, it leaves a drawn line of the playhead in its place. Narrower than
/// <see cref="NarrowWidth"/>, the length, loop and Volume go behind one button, so the
/// seek bar keeps room to be aimed at.
/// </remarks>
internal sealed class TransportRow
{
    /// <summary>How tall the row is, and how wide and tall every button in it.</summary>
    public const double Reach = 44;

    /// <summary>Below this many pixels wide the row folds the length, loop and Volume away.</summary>
    public const double NarrowWidth = 560;

    public const string PauseTip = "Pause the patch, in the picture and in the sound.  (Ctrl+P)";

    public const string PlayTip = "Play the patch on from where it stopped.  (Ctrl+P)";

    public const string RewindTip = "Take the patch back to zero seconds, in the picture and in the sound.";

    public const string MoreTip = "The patch's length, the loop switch and the Volume.";

    private readonly Grid row;
    private readonly Border bar;
    private readonly Grid folded;

    /// <summary>The columns the length, loop and Volume stand in while the row is wide.</summary>
    private readonly (Control Control, int Column, Thickness Margin)[] foldable;

    private bool narrow;

    public TransportRow(SeekBar seek, VolumeSlider volume, Reactions reactions, EditorHost host)
    {
        Seek = seek;
        Volume = volume;

        Pause = Button("pause", "Pause", Glyphs.Pause(), PauseTip);
        Rewind = Button("rewind", "Rewind", Glyphs.Rewind(), RewindTip);

        // What the record tip says is decided per patch by TakeRecording.Mark.
        Record = Button("record", "Record", Glyphs.Record(), TakeRecording.RecordTip);
        ToolTip.SetShowOnDisabled(Record, true);

        Measure = Button("measure", "Measure", Glyphs.Measure(), MeasureTip);

        Fit(seek.Loop);
        AutomationProperties.SetName(seek.Loop, "Loop");
        AutomationProperties.SetName(seek.Track, "Seek");
        AutomationProperties.SetName(seek.Length, "Length");
        AutomationProperties.SetName(volume.Slider, "Volume");

        Pause.Click += (_, _) => reactions.Raise(new PauseAsked());
        Rewind.Click += (_, _) => reactions.Raise(new RewindAsked());
        Record.Click += (_, _) => reactions.Raise(new RecordAsked());
        Measure.Click += (_, _) => reactions.Raise(new MeasureAsked());

        // Named beside each, since the glyphs that say what they are stay in the row.
        folded = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 8,
            Margin = new Thickness(4),
            MinWidth = 240,
        };

        Label("Length", 0);
        Label("Loop", 1);
        Label("Volume", 2).Bind(Visual.IsVisibleProperty, volume.View.GetObservable(Visual.IsVisibleProperty));

        More = Button("transport-more", "Length, loop and Volume", Glyphs.Dots(), MoreTip);
        More.IsVisible = false;
        More.Flyout = new Flyout { Content = folded, Placement = PlacementMode.TopEdgeAlignedRight };

        row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 4,
            Margin = new Thickness(8, 0),
            Height = Reach,
        };

        foldable = [(seek.Length, 4, new Thickness(6, 0, 0, 0)), (seek.Loop, 5, default), (volume.View, 6, new Thickness(6, 0, 4, 0))];

        Place(Pause, 0);
        Place(Rewind, 1);
        Place(seek.Position, 2);
        Place(seek.Track, 3);
        foreach (var (control, column, margin) in foldable)
        {
            control.Margin = margin;
            Place(control, column);
        }
        Place(More, 7);
        if (!host.InPage)
        {
            Place(Measure, 8);
            Place(Record, 9);
        }

        seek.Position.Margin = new Thickness(4, 0, 6, 0);

        row.SizeChanged += (_, e) => Fold(e.NewSize.Width < NarrowWidth);

        bar = new Border
        {
            Name = "transport",
            Background = new SolidColorBrush(Colors.Toolbar),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = row,
        };

        seek.Line.IsVisible = false;

        View = new StackPanel { Children = { seek.Line, bar } };
    }

    public Button Pause { get; }

    public Button Rewind { get; }

    /// <summary>Starts and stops a take (ADR-0080). Only outside a page.</summary>
    public Button Record { get; }

    /// <summary>Runs the patch for a few seconds from the playhead and pins what each output carries. Only outside a page.</summary>
    public Button Measure { get; }

    private const string MeasureTip =
        "Measure: run the patch for a few seconds from the playhead and pin what each output carries beside it, "
        + "the selected modules' or every module's  (Ctrl+M)";

    /// <summary>What the row cannot fit while narrow, behind one button.</summary>
    public Button More { get; }

    public SeekBar Seek { get; }

    public VolumeSlider Volume { get; }

    /// <summary>The row, or its playhead line while it is hidden.</summary>
    public Control View { get; }

    /// <summary>Whether the row is shown, rather than the line standing in for it.</summary>
    public bool Shown
    {
        get => bar.IsVisible;
        set
        {
            bar.IsVisible = value;
            Seek.Line.IsVisible = !value;
        }
    }

    /// <summary>Whether the length, loop and Volume are behind <see cref="More"/>.</summary>
    public bool Narrow => narrow;

    private void Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(control);
    }

    /// <summary>Moves the length, loop and Volume behind the button, or back into the row.</summary>
    private void Fold(bool value)
    {
        if (value == narrow) return;

        narrow = value;
        More.IsVisible = value;

        for (var at = 0; at < foldable.Length; at++)
        {
            var (control, column, margin) = foldable[at];

            (control.Parent as Panel)?.Children.Remove(control);

            if (value)
            {
                control.HorizontalAlignment = HorizontalAlignment.Left;
                control.Margin = default;
                Grid.SetColumn(control, 1);
                Grid.SetRow(control, at);
                folded.Children.Add(control);
            }
            else
            {
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                control.Margin = margin;
                Place(control, column);
            }
        }
    }

    /// <summary>Names a row of what is folded behind <see cref="More"/>.</summary>
    private TextBlock Label(string text, int at)
    {
        var label = new TextBlock { Text = text, FontSize = Text.Body, Foreground = Text.Muted, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetRow(label, at);
        folded.Children.Add(label);
        return label;
    }

    private static Button Button(string name, string label, Control icon, string tip)
    {
        var button = Fit(ToolbarButtons.Drawn(name, icon, tip));

        AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>A button a finger can find: the row's height square, with the glyph alone drawn.</summary>
    private static T Fit<T>(T button)
        where T : ContentControl
    {
        button.Width = button.Height = Reach;
        button.Background = Brushes.Transparent;
        return button;
    }
}
