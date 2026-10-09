using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Gallery;

/// <summary>
/// The gallery's three columns: what narrows it, the cards, and the chosen card
/// with the button that opens it. Too narrow for three, the filter box and the
/// halves of the Output go above the cards and the chosen card's name and button below.
/// </summary>
internal sealed class GalleryLayout
{
    private const double Narrowest = 780;

    private const double SideWidth = 216;

    private const double DetailWidth = 300;

    private readonly GalleryChoice choice;
    private readonly PatchPreset? showing;

    private readonly Grid grid = new();
    private readonly Border side;
    private readonly ScrollViewer cards;
    private readonly Border detail;
    private readonly Border bar;
    private readonly Border foot;

    private readonly Panel boxWide = new Panel();
    private readonly Panel boxNarrow = new Panel();
    private readonly Panel useWide = new Panel();
    private readonly Panel useNarrow = new Panel { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button use;

    private readonly TextBlock tally = new() { Name = "preset-tally", FontSize = Text.Caption, Foreground = Text.Muted, Margin = new Thickness(4, 0) };
    private readonly List<(string? Section, Button Row, TextBlock Count)> sectionRows = [];
    private readonly List<(PresetWorks Kind, Button Row, TextBlock Count)> worksRows = [];
    private readonly List<(PresetWorks Kind, Button Chip, TextBlock Count)> worksChips = [];
    private readonly WrapPanel topics = new() { Name = "topics", ItemSpacing = 6, LineSpacing = 6 };
    private readonly Control topicsGroup;
    private readonly Button clear;

    private readonly Image picture = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock pictureWords = new() { FontSize = Text.Small, Foreground = Text.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl speaker = new()
    {
        Foreground = Text.Muted,
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Content = new Viewbox { Width = 44, Height = 44, Child = Glyphs.Speaker() },
    };
    private readonly Border pictureFrame;
    private readonly TextBlock section = new() { Name = "detail-section", FontSize = Text.Caption, Foreground = Text.Muted };
    private readonly TextBlock name = new() { Name = "detail-name", FontSize = Text.Display, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel badges = new() { Name = "detail-badges", Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly TextBlock description = new() { Name = "detail-description", FontSize = Text.Emphasis, LineHeight = 20, Foreground = new SolidColorBrush(Colors.Label), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock credit = new() { Name = "detail-credit", FontSize = Text.Caption, FontStyle = FontStyle.Italic, Foreground = Text.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly Border note;
    private readonly WrapPanel tags = new() { Name = "detail-topics", ItemSpacing = 6, LineSpacing = 6 };
    private readonly Control tagsGroup;
    private readonly TextBlock chosenName = new() { Name = "chosen-name", FontSize = Text.Heading, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>The topics the row of chips was last built from, so it is rebuilt only when they change.</summary>
    private (int Count, string? Pressed) topicsBuilt = (-1, null);

    private bool? narrow;

    public GalleryLayout(GalleryChoice choice, Control main, PatchPreset? showing, string useLabel)
    {
        this.choice = choice;
        this.showing = showing;

        use = new Button
        {
            Name = "use-preset",
            Content = useLabel,
            Height = 40,
            MinWidth = 150,
            Padding = new Thickness(18, 0),
            CornerRadius = new CornerRadius(10),
            FontSize = Text.Emphasis,
            FontWeight = FontWeight.SemiBold,
            Background = new SolidColorBrush(PresetGallery.Accent),
            Foreground = new SolidColorBrush(Colors.Edge),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(use, "Open the chosen preset  (Enter, or double-click its card)");
        use.Click += (_, _) => choice.OpenChosen();

        clear = new Button
        {
            Name = "clear-filters",
            Content = "Clear filters",
            FontSize = Text.Body,
            Padding = new Thickness(10, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(PresetGallery.Accent),
        };

        clear.Click += (_, _) => choice.Clear();

        sectionRows.Add((null, NavRow("All presets", null, () => choice.ShowSection(null), out var all), all));

        foreach (var heading in choice.Sections)
            sectionRows.Add((heading, NavRow(Sentence(heading), null, () => choice.ShowSection(heading), out var count), count));

        foreach (var (kind, label, dot) in Halves())
        {
            worksRows.Add((kind, NavRow(label, dot, () => choice.ShowKind(kind), out var count), count));
            worksChips.Add((kind, KindChip(label, () => choice.ShowKind(kind), out var chipCount), chipCount));
        }

        topicsGroup = Group("TOPICS", topics);

        side = new Border
        {
            Name = "preset-side",
            Background = new SolidColorBrush(Colors.Window),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new StackPanel
                {
                    Margin = new Thickness(12, 14, 12, 20),
                    Spacing = 18,
                    Children =
                    {
                        new StackPanel { Spacing = 6, Children = { boxWide, tally } },
                        Group("SECTION", Rows(sectionRows.Select(row => row.Row))),
                        Group("WORKS WITH", Rows(worksRows.Select(row => row.Row))),
                        topicsGroup,
                        clear,
                    },
                },
            },
        };

        cards = new ScrollViewer
        {
            Name = "gallery-scroll",
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = main,
        };

        pictureFrame = new Border
        {
            Name = "detail-picture",
            Height = 158,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = new SolidColorBrush(Colors.Canvas),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            Child = new Grid { Children = { picture, pictureWords, speaker } },
        };

        note = new Border
        {
            Name = "detail-note",
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            Background = new SolidColorBrush(Colors.Canvas),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = "On the canvas now.", FontSize = Text.Body, Foreground = new SolidColorBrush(Colors.Label) },
        };

        tagsGroup = Group("TOPICS", tags);

        detail = new Border
        {
            Name = "preset-detail",
            Background = new SolidColorBrush(Colors.Window),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = Docked(
                new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.Separator),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(16, 12, 16, 16),
                    Child = useWide,
                },
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(18),
                        Spacing = 14,
                        Children =
                        {
                            pictureFrame,
                            new StackPanel { Spacing = 6, Children = { section, name, badges } },
                            description,
                            credit,
                            note,
                            tagsGroup,
                        },
                    },
                }),
        };

        bar = new Border
        {
            Name = "preset-bar",
            Background = new SolidColorBrush(Colors.Window),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    boxNarrow,
                    new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { worksChips[0].Chip, worksChips[1].Chip, worksChips[2].Chip } },
                },
            },
        };

        var chosen = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "CHOSEN", FontSize = Text.Caption, Foreground = Text.Muted }, chosenName },
        };

        var footing = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };

        Grid.SetColumn(useNarrow, 1);
        footing.Children.Add(chosen);
        footing.Children.Add(useNarrow);

        foot = new Border
        {
            Name = "preset-foot",
            Background = new SolidColorBrush(Colors.Window),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 10, 16, 14),
            Child = footing,
        };

        grid.Children.Add(side);
        grid.Children.Add(cards);
        grid.Children.Add(detail);
        grid.Children.Add(bar);
        grid.Children.Add(foot);

        View = new Border
        {
            Name = "preset-picker",
            MinWidth = 300,
            Margin = new Thickness(0, 8, 0, 0),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };

        View.SizeChanged += (_, e) => Arrange(e.NewSize.Width < Narrowest);

        choice.Narrowed += Refresh;
        choice.Changed += Describe;

        Arrange(narrow: false);
        Refresh();
    }

    public Border View { get; }

    private static IEnumerable<(PresetWorks Kind, string Label, Color Dot)> Halves() =>
    [
        (PresetWorks.Anything, "Anything", Colors.Inactive),
        (PresetWorks.Sound, "Sound", PresetCard.Heard),
        (PresetWorks.Picture, "Picture", PresetCard.Seen),
    ];

    /// <summary>Puts the columns, the filter box and the button where a window this wide wants them.</summary>
    private void Arrange(bool narrow)
    {
        if (this.narrow == narrow) return;

        this.narrow = narrow;

        var typing = choice.Box.IsFocused;

        Move(choice.Box, narrow ? boxNarrow : boxWide);
        Move(use, narrow ? useNarrow : useWide);

        grid.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions($"{SideWidth},*,{DetailWidth}");
        grid.RowDefinitions = narrow ? new RowDefinitions("Auto,*,Auto") : new RowDefinitions("*");

        side.IsVisible = detail.IsVisible = !narrow;
        bar.IsVisible = foot.IsVisible = narrow;

        Grid.SetColumn(side, 0);
        Grid.SetColumn(detail, 2);
        Grid.SetColumn(cards, narrow ? 0 : 1);
        Grid.SetRow(cards, narrow ? 1 : 0);
        Grid.SetRow(foot, 2);

        if (typing) Dispatcher.UIThread.Post(() => choice.Box.Focus());
    }

    private static void Move(Control part, Panel to)
    {
        if (part.Parent == to) return;

        (part.Parent as Panel)?.Children.Remove(part);
        to.Children.Add(part);
    }

    /// <summary>Brings the counts, the pressed rows and the topics up to what the choice shows.</summary>
    private void Refresh()
    {
        var (shown, of) = choice.Tally;

        tally.Text = choice.Narrowing ? $"{shown} of {of} presets" : $"{of} presets";

        foreach (var (heading, row, count) in sectionRows)
        {
            var local = heading != PresetGallery.SiteHeading;

            count.Text = local ? Said(choice.Count(heading, choice.Kind)) : string.Empty;
            Press(row, choice.Section == heading);
        }

        foreach (var (kind, row, count) in worksRows)
        {
            count.Text = Said(choice.Count(choice.Section, kind));
            Press(row, choice.Kind == kind);
        }

        foreach (var (kind, chip, count) in worksChips)
        {
            count.Text = Said(choice.Count(choice.Section, kind));
            Press(chip, choice.Kind == kind);
        }

        if (topicsBuilt != (choice.Topics.Count, choice.Topic))
        {
            topicsBuilt = (choice.Topics.Count, choice.Topic);
            topics.Children.Clear();

            foreach (var topic in choice.Topics)
                topics.Children.Add(Chip(topic, string.Equals(choice.Topic, topic, StringComparison.OrdinalIgnoreCase), () => choice.ToggleTopic(topic)));
        }

        topicsGroup.IsVisible = choice.Topics.Count > 0;
        clear.IsVisible = choice.Narrowing;

        Describe();
    }

    /// <summary>Says what the chosen card is, in the column after and along the foot.</summary>
    private void Describe()
    {
        var card = choice.Chosen;

        use.IsEnabled = card is not null;
        chosenName.Text = card?.Preset.Name ?? "Nothing chosen";

        pictureFrame.IsVisible = badges.IsVisible = card is not null;

        if (card is null)
        {
            section.Text = string.Empty;
            name.Text = "Nothing chosen";
            description.Text = "Choose a preset to read about it here.";
            description.IsVisible = true;
            credit.IsVisible = note.IsVisible = tagsGroup.IsVisible = false;
            return;
        }

        picture.Source = card.Picture.Source;
        pictureWords.Text = card.Words.Text;
        speaker.IsVisible = card.Speaker.IsVisible;

        section.Text = Sentence(card.Section);
        name.Text = card.Preset.Name;
        PresetGallery.Badges(badges, card.Said?.Reaches);

        description.Text = card.Description.Text;
        description.IsVisible = description.Text is { Length: > 0 };

        credit.Text = card.Said?.Author is { } author ? "by " + author : string.Empty;
        credit.IsVisible = credit.Text.Length > 0;

        note.IsVisible = card.Preset == showing;

        tags.Children.Clear();

        foreach (var tag in card.Tags)
            tags.Children.Add(Chip(tag, string.Equals(choice.Topic, tag, StringComparison.OrdinalIgnoreCase), () => choice.ToggleTopic(tag)));

        tagsGroup.IsVisible = tags.Children.Count > 0;
    }

    /// <summary>A heading as a sentence, for where it names a row rather than heads a run.</summary>
    private static string Sentence(string heading) =>
        heading.Length == 0 ? heading : heading[0] + heading[1..].ToLower(CultureInfo.InvariantCulture);

    private static string Said(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static DockPanel Docked(Control bottom, Control fill)
    {
        DockPanel.SetDock(bottom, Dock.Bottom);

        return new DockPanel { Children = { bottom, fill } };
    }

    private static StackPanel Rows(IEnumerable<Button> rows)
    {
        var panel = new StackPanel { Spacing = 2 };

        foreach (var row in rows) panel.Children.Add(row);

        return panel;
    }

    private static StackPanel Group(string heading, Control content) => new()
    {
        Spacing = 6,
        Children =
        {
            new TextBlock
            {
                Text = heading,
                FontSize = Text.Caption,
                FontWeight = FontWeight.SemiBold,
                Foreground = Text.Muted,
                Margin = new Thickness(10, 0, 0, 0),
            },
            content,
        },
    };

    /// <summary>A row of the column that narrows the gallery: its label, a dot where it has one, and a count.</summary>
    private static Button NavRow(string label, Color? dot, Action pick, out TextBlock count)
    {
        count = new TextBlock { FontSize = Text.Caption, Foreground = Text.Muted, VerticalAlignment = VerticalAlignment.Center };

        var words = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        if (dot is { } color)
            words.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });

        words.Children.Add(new TextBlock { Text = label, FontSize = Text.Body });

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        Grid.SetColumn(count, 1);
        content.Children.Add(words);
        content.Children.Add(count);

        var row = new Button
        {
            Name = "filter-row",
            Tag = label,
            MinHeight = 32,
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = content,
        };

        row.Click += (_, _) => pick();

        return row;
    }

    /// <summary>A half of the Output as a chip, with its count, for the narrow layout.</summary>
    private static Button KindChip(string label, Action pick, out TextBlock count)
    {
        count = new TextBlock { FontSize = Text.Caption, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };

        var chip = Chip(label, false, pick);

        chip.Name = "kind-chip";
        chip.MinHeight = 36;
        chip.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = label, FontSize = Text.Body, VerticalAlignment = VerticalAlignment.Center }, count },
        };

        return chip;
    }

    /// <summary>A rounded button that shows only what it names, filled while it does.</summary>
    private static Button Chip(string label, bool pressed, Action pick)
    {
        var chip = new Button
        {
            Name = "topic",
            Tag = label,
            Content = label,
            FontSize = Text.Caption,
            MinHeight = 26,
            Padding = new Thickness(10, 3),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        Press(chip, pressed);
        chip.Click += (_, _) => pick();

        return chip;
    }

    /// <summary>Paints a row or a chip as showing what it names, or not.</summary>
    private static void Press(Button button, bool pressed)
    {
        if (button.Name == "filter-row")
        {
            button.Background = pressed ? new SolidColorBrush(Colors.Node) : Brushes.Transparent;
            button.Foreground = new SolidColorBrush(pressed ? Colors.BeamCore : Colors.Label);
        }
        else
        {
            button.Background = pressed ? new SolidColorBrush(PresetGallery.Accent) : Brushes.Transparent;
            button.BorderBrush = new SolidColorBrush(pressed ? PresetGallery.Accent : Colors.Separator);
            button.Foreground = new SolidColorBrush(pressed ? Colors.Edge : Colors.Label);
        }

        button.Classes.Set("pressed", pressed);
    }
}
