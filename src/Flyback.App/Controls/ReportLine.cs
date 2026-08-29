using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Flyback.App.Controls;

/// <summary>
/// The status bar's one line of prose, and the log of everything it has said.
/// </summary>
/// <remarks>
/// <para>
/// A line on a status bar is only as wide as the window leaves it, and the
/// things this program has to say — where plugins are looked for, the four
/// problems one compile found at once — are routinely longer than that. Cut off
/// at the edge it reads as a sentence that stops mid-word, with nothing to say
/// there was more of it; trimmed to an ellipsis it reads as a sentence there is
/// more of, which is both the truth and the invitation to click.
/// </para>
/// <para>
/// The log behind it is here rather than beside it, because this line is the one
/// place in the program where something is said and then taken away again: the
/// next compile clears it whether or not anybody was looking. Keeping what was
/// said, and when, is what turns a line you may have missed into one you can go
/// back to — and it is the same list either way, so the control that shows the
/// newest is the one that holds the rest.
/// </para>
/// </remarks>
internal sealed class ReportLine : UserControl
{
    /// <summary>
    /// How many past messages are kept.
    /// </summary>
    /// <remarks>
    /// Few, and deliberately. This is here to catch the line you were reading
    /// when the next one replaced it, not to be a record of the session: what
    /// happened five messages ago has been overtaken by five things since, and a
    /// list long enough to scroll would only be a worse way of asking the same
    /// short question.
    /// </remarks>
    private const int Remembered = 5;

    private const double PopupWidth = 440;

    /// <summary>How tall the list gets before it scrolls.</summary>
    private const double TallestList = 320;

    /// <summary>
    /// The class the flyout presenter holding the log is given, so that
    /// <see cref="Trim"/> can find it.
    /// </summary>
    public const string PresenterClass = "report";

    /// <summary>What the line says it is for while there is nothing on it.</summary>
    private const string Hint = "Click to see what was said earlier.";

    /// <summary>
    /// Between two things said at once, on the one line there is to say them on.
    /// The log has a row each, so this is only ever seen on the bar.
    /// </summary>
    private const string Separator = "  •  ";

    /// <summary>One thing said, and the moment it was said at.</summary>
    private sealed record Entry(DateTime At, string Message, string? Detail, bool Progress);

    private readonly List<Entry> log = [];

    /// <summary>
    /// What is on the line at this moment — several things, when several were
    /// said at once. It is what tells a message that is still true from one that
    /// has been overtaken, and what stops a compile that finds the same problem
    /// after every edit from writing it down after every edit.
    /// </summary>
    private readonly List<string> spoken = [];

    private readonly TextBlock line = new()
    {
        VerticalAlignment = VerticalAlignment.Center,

        // The whole point of this control. A status bar hands out whatever width
        // is left over, and the only honest thing a long line can do with too
        // little of it is say so.
        TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = new SolidColorBrush(Colors.Attention),
    };

    private readonly StackPanel entries = new() { Spacing = 10 };

    private readonly ScrollViewer scroll = new()
    {
        MaxHeight = TallestList,

        // The rows wrap, so there is never anything to reach sideways for — and
        // a horizontal bar under a list of sentences is one that would never be
        // used.
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    private readonly Flyout flyout = new()
    {
        // Above, because this line lives along the bottom edge of the window and
        // a popup placed under it would have nowhere to go.
        Placement = PlacementMode.Top,
    };

    public ReportLine()
    {
        // A test finds this by name; there is nothing else on the status bar
        // that a phrase would tell it apart from.
        Name = "report";

        // Transparent rather than unset, which is the difference between a strip
        // that can be clicked along its whole length and one that can only be
        // hit on the letters. With the hand cursor it is also the only thing
        // saying the line is clickable while it is empty.
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        VerticalAlignment = VerticalAlignment.Center;

        Content = line;
        ToolTip.SetTip(this, Hint);

        flyout.Content = BuildPopup();
        flyout.FlyoutPresenterClasses.Add(PresenterClass);
    }

    /// <summary>
    /// What the flyout presenter around the log needs told, for whoever is
    /// putting one of these on a window to add to its styles.
    /// </summary>
    /// <remarks>
    /// The presenter brings its own padding and its own scrolling, and between
    /// them they put a horizontal scrollbar under the list: the padding made the
    /// content wider than the box it was measured against, by exactly the
    /// padding. The popup is a fixed width that pads itself, so the presenter's
    /// share of both is nothing — and the list caps its own height, so there is
    /// never anything for the presenter to scroll in either direction.
    /// </remarks>
    public static Style Trim()
    {
        var style = new Style(x => x.OfType<FlyoutPresenter>().Class(PresenterClass));

        style.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(
            ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        style.Setters.Add(new Setter(
            ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));

        return style;
    }

    /// <summary>
    /// Raised for each message the first time it is said, for wherever else a
    /// message is worth having — the terminal, today.
    /// </summary>
    /// <remarks>
    /// An event rather than a write from in here, because a control's business
    /// is the showing of a thing and not the filing of it: what the other copies
    /// are and where they go is a decision for whoever put this on a window, and
    /// a second one is one more line there.
    /// <para>
    /// Once per thing said, not once per time it is repeated — a compile says
    /// its whole list again after every edit, and the same filtering that keeps
    /// the popup readable is what this fires from. Progress is left out
    /// altogether: a line that says 4%, then 5%, then 6% is one event a second
    /// for a fact nobody is reading afterwards.
    /// </para>
    /// </remarks>
    public event EventHandler<string>? Said;

    /// <summary>What has been said, in the order it was said.</summary>
    internal IReadOnlyList<string> History => log.Select(entry => entry.Message).ToList();

    /// <summary>
    /// Puts <paramref name="message"/> on the line and into the log.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="detail">
    /// What will not fit on a status bar — a list of missing plugins, say. It is
    /// kept with the message rather than only hung off a tooltip, so it is still
    /// there once the line has moved on.
    /// </param>
    /// <param name="progress">
    /// That this is the previous sentence with a new number in it. Such a line
    /// replaces its predecessor in the log instead of being added beside it, so
    /// an export leaves one entry rather than two hundred.
    /// </param>
    public void Say(string message, string? detail = null, bool progress = false) =>
        Say([message], detail, progress);

    /// <summary>
    /// The same, for everything there is to say at once — a compile that found
    /// four problems has four things to say, not one sentence with three
    /// bullets in it.
    /// </summary>
    /// <remarks>
    /// They share the line, because there is only one, and it joins them. They
    /// do not share a row in the log: each is its own problem, arrives and is
    /// fixed on its own, and reads as one thing to deal with rather than as part
    /// of a paragraph.
    /// </remarks>
    public void Say(IReadOnlyList<string> messages, string? detail = null, bool progress = false)
    {
        var saying = messages
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .ToList();

        // Nothing to report is reported by saying nothing, and is not something
        // to remember: a compile that found no problems clears this line after
        // every edit.
        if (saying.Count == 0)
        {
            line.Text = string.Empty;
            spoken.Clear();
            ToolTip.SetTip(this, Hint);
            return;
        }

        // Only what was not already up. Recompiling repeats its list after every
        // edit, and a log of the same problem forty times says less than one of
        // it does — while a problem that has just joined the list is news.
        if (saying.FirstOrDefault(m => !spoken.Contains(m)) is not null)
        {
            if (progress && log is [.., { Progress: true }]) log.RemoveAt(log.Count - 1);

            // One moment, however many of them there were: they were all said at
            // once, and stamping them a tick apart would claim an order they
            // were never in.
            var at = DateTime.Now;

            foreach (var message in saying.Where(m => !spoken.Contains(m)))
            {
                log.Add(new Entry(at, message, detail, progress));

                if (!progress) Said?.Invoke(this, message);
            }

            if (log.Count > Remembered) log.RemoveRange(0, log.Count - Remembered);
        }

        spoken.Clear();
        spoken.AddRange(saying);

        line.Text = string.Join(Separator, saying);

        // Still on the tooltip as well as in the popup: reading a trimmed line
        // is worth a hover, and should not cost a click.
        ToolTip.SetTip(this, detail is { Length: > 0 } ? $"{line.Text}\n\n{detail}" : line.Text);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Filled here rather than on the flyout's own opening, so that what is
        // shown is what the log held at the moment of the click and there is no
        // second event to keep in step with it.
        Fill();
        flyout.ShowAt(this);

        // At the bottom, which is where the newest is and where a list opened
        // too short to hold all of it would otherwise not be. After the layout
        // rather than with it, because until the popup has been measured there
        // is no end to scroll to.
        Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Loaded);

        e.Handled = true;
    }

    private Control BuildPopup()
    {
        scroll.Content = entries;

        // The width and the inset both belong to this one box, so that what the
        // presenter is asked to hold is exactly as wide as it says it is. Split
        // between the two — a width here and padding around it — the padding is
        // added to the width and the list overflows by it.
        return new Border
        {
            Width = PopupWidth,
            Padding = new Thickness(12),
            Child = scroll,
        };
    }

    private void Fill()
    {
        entries.Children.Clear();

        if (log.Count == 0)
        {
            entries.Children.Add(new TextBlock
            {
                Text = "Nothing has been reported yet.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Inactive),
            });

            return;
        }

        // Which rows are still on the bar — all of them, when several things
        // were said at once. Only a message that is still up is still true, and
        // an amber row under a status bar that has gone quiet says the opposite
        // of what happened. The last of each, because a problem that was fixed
        // and came back is the newer row rather than both.
        var standing = spoken
            .Select(message => log.FindLastIndex(entry => entry.Message == message))
            .Where(i => i >= 0)
            .ToHashSet();

        // In the order it was said, so the newest is at the bottom — nearest the
        // line it came off, and where a run of messages was watched arriving.
        for (var i = 0; i < log.Count; i++)
            entries.Children.Add(Row(log[i], current: standing.Contains(i)));
    }

    /// <summary>One row of the log: when, and what.</summary>
    /// <param name="entry"></param>
    /// <param name="current">
    /// Whether this is the line still on the status bar, which is shown in its
    /// own color so that what is still true is told apart from what merely was.
    /// Nothing is, once the line has been cleared.
    /// </param>
    private static Control Row(Entry entry, bool current)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

        row.Children.Add(new TextBlock
        {
            Text = entry.At.ToString("HH:mm:ss"),
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Colors.Inactive),
            Margin = new Thickness(0, 1, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
        });

        var text = new StackPanel { Spacing = 3 };
        Grid.SetColumn(text, 1);

        text.Children.Add(new TextBlock
        {
            Text = entry.Message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(current ? Colors.Attention : Colors.Muted),
        });

        if (entry.Detail is { Length: > 0 } detail)
        {
            text.Children.Add(new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Colors.Inactive),
            });
        }

        row.Children.Add(text);

        return row;
    }
}
