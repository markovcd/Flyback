using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Gallery;

/// <summary>
/// What narrows the gallery and which card is chosen: the filter box, a section,
/// the half of the Output and a topic, and the module palette's keys.
/// </summary>
/// <remarks>
/// Hides cards rather than rebuilding them, because a card carries a picture drawn
/// off the UI thread and may be playing one. A run is hidden with its heading, so
/// no heading stands over nothing. The focus stays in the box while the arrows move
/// the choice, so typing goes on narrowing while they move.
/// </remarks>
internal sealed class GalleryChoice
{
    /// <summary>The color of what is chosen and of the button that opens it.</summary>
    public static Color Accent => Colors.Feedback;

    private readonly List<(string Section, Control Heading, TextBlock Count, Panel Tiles)> runs = [];

    private readonly Dictionary<Button, PresetCard> cards = [];

    /// <summary>The cards showing, in the order they are shown, which is what the arrows walk.</summary>
    private readonly List<PresetCard> listed = [];

    /// <summary>Every topic a patch has said, in the order they were first said.</summary>
    private readonly List<string> topics = [];

    /// <summary>Runs that list what is not on this machine, shown or hidden whole.</summary>
    private readonly List<(string Section, Control View)> elsewhere = [];

    public TextBox Box { get; } = new()
    {
        Name = "preset-filter",
        PlaceholderText = "Filter presets",
        FontSize = Text.Body,
    };

    /// <summary>Whether presets from elsewhere follow, so the hint says it means the ones here.</summary>
    public bool Elsewhere { get; init; }

    /// <summary>Whether the box takes the keyboard on opening; not under a finger, where it waits to be tapped.</summary>
    public bool Typing { get; init; } = true;

    public TextBlock Hint { get; } = new()
    {
        Name = "preset-hint",
        TextWrapping = TextWrapping.Wrap,
        FontSize = Text.Body,
        Foreground = Text.Muted,
        IsVisible = false,
    };

    /// <summary>Opens a preset, and so answers the gallery.</summary>
    public Action<IPreset?> Open { get; set; } = _ => { };

    /// <summary>The heading of the one section shown, or null for all of them.</summary>
    public string? Section { get; private set; }

    public PresetWorks Kind { get; private set; }

    /// <summary>The one topic shown, or null for any.</summary>
    public string? Topic { get; private set; }

    /// <summary>The card the column after describes, and what Enter opens.</summary>
    public PresetCard? Chosen { get; private set; }

    /// <summary>Raised whenever what is shown, or what is known to count, has changed.</summary>
    public event Action? Narrowed;

    /// <summary>Raised whenever <see cref="Chosen"/> or what is known of it has changed.</summary>
    public event Action? Changed;

    /// <summary>Every section, in the order the gallery lists them.</summary>
    public IEnumerable<string> Sections => runs.Select(run => run.Section).Concat(elsewhere.Select(run => run.Section));

    public IReadOnlyList<string> Topics => topics;

    /// <summary>How many presets on this machine there are, and how many are shown.</summary>
    public (int Shown, int Of) Tally => (listed.Count, cards.Count);

    /// <summary>Whether anything at all narrows what is shown.</summary>
    public bool Narrowing => Query.Length > 0 || Section is not null || Kind != PresetWorks.Anything || Topic is not null;

    private string Query => Box.Text?.Trim() ?? string.Empty;

    public GalleryChoice()
    {
        Box.TextChanged += (_, _) => Apply(typed: true);

        Box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down:
                    Move(+1);
                    break;

                case Key.Up:
                    Move(-1);
                    break;

                case Key.Enter:
                    OpenChosen();
                    break;

                // Empties the box; an empty one lets the key through, so a
                // second press closes the gallery — one key, always a step back.
                case Key.Escape when Box.Text is { Length: > 0 }:
                    Box.Text = string.Empty;
                    break;

                default:
                    return;
            }

            e.Handled = true;
        };

        // Posted, because the dialog gives its sheet the focus as it goes up,
        // which is after this is put in it.
        Box.AttachedToVisualTree += (_, _) =>
        {
            if (Typing) Dispatcher.UIThread.Post(() => Box.Focus());
        };
    }

    public void Add(string section, Control heading, TextBlock count, Panel tiles) => runs.Add((section, heading, count, tiles));

    public void AddElsewhere(string section, Control view)
    {
        elsewhere.Add((section, view));
        Apply();
    }

    /// <summary>Makes a card choosable: a click chooses it, and a double-click or Enter on it opens it.</summary>
    public void Track(PresetCard card)
    {
        var tile = card.Button;

        cards[tile] = card;

        // A tile being asked about is not one to choose: a click anywhere on
        // it while the question is up is a miss for the answers.
        tile.Click += (_, _) =>
        {
            if (!tile.Classes.Contains(PresetGallery.Asking)) Choose(card);
        };

        tile.DoubleTapped += (_, _) =>
        {
            if (tile.Classes.Contains(PresetGallery.Asking)) return;

            Choose(card);
            OpenChosen();
        };

        // Ahead of the button, which would otherwise take Enter as a click.
        tile.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || tile.Classes.Contains(PresetGallery.Asking)) return;

            Choose(card);
            OpenChosen();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>Forgets the cards in <paramref name="tiles"/>, which are about to be rebuilt.</summary>
    public void Drop(Panel tiles)
    {
        foreach (var child in tiles.Children)
        {
            if (child is not Button tile || !cards.Remove(tile, out var card)) continue;

            if (card == Chosen) Chosen = null;
        }
    }

    /// <summary>
    /// Takes in what a card's patch has said: its topics join the list, and it may
    /// match what is typed or chosen now.
    /// </summary>
    public void Said(PresetCard card, Thumbnail said)
    {
        card.Said = said;

        foreach (var tag in said.Tags ?? [])
            if (!topics.Contains(tag, StringComparer.OrdinalIgnoreCase)) topics.Add(tag);

        Apply();

        if (card == Chosen) Changed?.Invoke();
    }

    /// <summary>A card's picture has arrived.</summary>
    public void Drawn(PresetCard card)
    {
        if (card == Chosen) Changed?.Invoke();
    }

    public void ShowSection(string? section)
    {
        Section = section;
        Apply();
    }

    public void ShowKind(PresetWorks kind)
    {
        Kind = kind;
        Apply();
    }

    /// <summary>Shows only <paramref name="topic"/>, or every topic again when it is the one shown.</summary>
    public void ToggleTopic(string topic)
    {
        Topic = string.Equals(Topic, topic, StringComparison.OrdinalIgnoreCase) ? null : topic;
        Apply();
    }

    /// <summary>Everything shown again.</summary>
    public void Clear()
    {
        Section = null;
        Kind = PresetWorks.Anything;
        Topic = null;

        if (Box.Text is { Length: > 0 }) Box.Text = string.Empty;
        else Apply();
    }

    /// <summary>How many cards would show in <paramref name="section"/> for <paramref name="kind"/>, with the rest as it is.</summary>
    public int Count(string? section, PresetWorks kind) => cards.Values.Count(card => Matches(card, section, kind));

    /// <summary>
    /// Shows what matches. A preset matches on its name, the heading it is under, its
    /// author or a topic, but not on its description: every preset has a sentence of
    /// prose, and matching it turns a search for a common word into most of the gallery.
    /// </summary>
    /// <param name="typed">
    /// Chooses the first match, so a few letters and Enter opens what was looked for.
    /// Otherwise the choice stays where it is while it is still shown.
    /// </param>
    public void Apply(bool typed = false)
    {
        listed.Clear();

        var anything = false;

        foreach (var (section, heading, count, tiles) in runs)
        {
            var any = false;
            var shown = 0;

            foreach (var child in tiles.Children)
            {
                // Anything that is not a preset is the card that saves one,
                // which nobody is looking for by name.
                var matched = child is Button tile && cards.TryGetValue(tile, out var card)
                    ? Matches(card, Section, Kind)
                    : Holds(section) && Kind == PresetWorks.Anything && Topic is null && (Query.Length == 0 || Has(section, Query));

                child.IsVisible = matched;
                any |= matched;

                if (!matched || child is not Button button || !cards.TryGetValue(button, out var listedCard)) continue;

                listed.Add(listedCard);
                shown++;
            }

            heading.IsVisible = tiles.IsVisible = any;
            count.Text = shown.ToString(System.Globalization.CultureInfo.InvariantCulture);
            anything |= any;
        }

        foreach (var (section, view) in elsewhere)
            view.IsVisible = Holds(section) && Kind == PresetWorks.Anything && Topic is null;

        var query = Query;

        Hint.Text = query.Length == 0
            ? "Nothing here matches. Clear the filters to see every preset."
            : Elsewhere ? $"No preset on this machine matches “{query}”." : $"Nothing matches “{query}”.";
        Hint.IsVisible = !anything && (Section is null || runs.Any(run => run.Section == Section));

        if (typed || Chosen is null || !listed.Contains(Chosen)) Choose(listed.FirstOrDefault());

        Narrowed?.Invoke();
    }

    /// <summary>Chooses the card of <paramref name="preset"/>, where it has one showing.</summary>
    public void Choose(PatchPreset? preset)
    {
        if (preset is not null && listed.FirstOrDefault(card => card.Preset == preset) is { } card) Choose(card);
    }

    public void Choose(PresetCard? card)
    {
        if (card == Chosen) return;

        if (Chosen is not null) Paint(Chosen, chosen: false);

        Chosen = card;

        if (card is not null)
        {
            Paint(card, chosen: true);
            Reveal(card.Button);
        }

        Changed?.Invoke();
    }

    /// <summary>Scrolls <paramref name="tile"/> into sight, waiting for its first layout when it has none yet.</summary>
    private static void Reveal(Button tile)
    {
        if (tile.Bounds.Width > 0)
        {
            tile.BringIntoView();
            return;
        }

        tile.LayoutUpdated += Laid;

        void Laid(object? sender, EventArgs e)
        {
            if (tile.Bounds.Width <= 0) return;

            tile.LayoutUpdated -= Laid;
            Dispatcher.UIThread.Post(tile.BringIntoView, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Opens the chosen card's preset, unless it is being asked about.</summary>
    public void OpenChosen()
    {
        if (Chosen is { } card && !card.Button.Classes.Contains(PresetGallery.Asking)) Open(card.Preset);
    }

    /// <summary>Moves the choice along what is shown, stopping at the ends as the module palette's does.</summary>
    private void Move(int by)
    {
        if (listed.Count == 0) return;

        var at = Chosen is null ? -1 : listed.IndexOf(Chosen);

        Choose(listed[Math.Clamp(at + by, 0, listed.Count - 1)]);
    }

    private bool Holds(string section) => Section is null || Section == section;

    private bool Matches(PresetCard card, string? section, PresetWorks kind)
    {
        if (section is not null && card.Section != section) return false;
        if (!card.WorksWith(kind)) return false;
        if (Topic is not null && !card.Tags.Contains(Topic, StringComparer.OrdinalIgnoreCase)) return false;

        var query = Query;

        return query.Length == 0
            || Has(card.Section, query)
            || Has(card.Preset.Name, query)
            || Has(card.Said?.Author, query)
            || card.Tags.Any(tag => Has(tag, query));
    }

    private static bool Has(string? said, string text) => said?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false;

    private static void Paint(PresetCard card, bool chosen)
    {
        card.Button.BorderBrush = chosen ? new SolidColorBrush(GalleryChoice.Accent) : Brushes.Transparent;
        card.Button.Background = chosen ? new SolidColorBrush(Colors.Node) : Brushes.Transparent;
    }
}
