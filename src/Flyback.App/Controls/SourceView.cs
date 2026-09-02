using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Language;

namespace Flyback.App.Controls;

/// <summary>
/// The patch as text, and the one gesture that turns it into the patch.
/// </summary>
/// <remarks>
/// <para>
/// It shows and it asks; it does not build. Whether a source reads, and what
/// becomes of it if it does, is the shell's business — this raises
/// <see cref="EvaluateRequested"/> and is handed back a <see cref="LanguageLoad"/>
/// to show. That is what lets it be tested without a compiler and what keeps the
/// language's one entry point in one place.
/// </para>
/// <para>
/// A plain <see cref="TextBox"/> rather than an editor with a gutter and
/// highlighting. Avalonia's box has no rich text in it, so a gutter means a
/// second control scrolled in step with a scroll viewer reached for through the
/// template — worth having and not worth having first. What it costs is answered
/// where it hurts: a complaint carries a line and a column
/// (<see cref="LanguageIssue"/>), and clicking one puts the caret there.
/// </para>
/// </remarks>
internal sealed class SourceView : UserControl
{
    private static readonly FontFamily Mono =
        new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    private readonly TextBox text = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        FontFamily = Mono,
        FontSize = 13,
        Name = "source",
        TextWrapping = TextWrapping.NoWrap,
        Background = new SolidColorBrush(Colors.Canvas),
        Foreground = new SolidColorBrush(Colors.Label),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 10),
        VerticalContentAlignment = VerticalAlignment.Top,
    };

    /// <summary>
    /// What the shell has to say about who owns this text, shown above it.
    /// </summary>
    /// <remarks>
    /// The one place a person is told that what they are looking at is a reading
    /// of a patch rather than the patch itself — see ADR-0068. Above the text
    /// rather than in the status bar, because it is a fact about this text and
    /// not about the last thing that happened.
    /// </remarks>
    private readonly TextBlock notice = new()
    {
        FontSize = 11,
        Margin = new Thickness(12, 6),
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Colors.Attention),
        IsVisible = false,
    };

    private readonly StackPanel complaints = new() { Margin = new Thickness(8, 6), Spacing = 2 };

    private readonly ScrollViewer complaintsScroll = new()
    {
        MaxHeight = 150,
        IsVisible = false,
        Background = new SolidColorBrush(Colors.Panel),
    };

    private readonly TextBlock footer = new()
    {
        FontSize = 11,
        Margin = new Thickness(12, 5),
        Foreground = new SolidColorBrush(Colors.Inactive),
    };

    public SourceView()
    {
        complaintsScroll.Content = complaints;

        // Enter belongs to the text box, so the gesture that applies a patch has
        // to be one the box does not want. Ctrl+Enter is what every live coding
        // environment uses for exactly this, and it is free here for the same
        // reason it is free there.
        //
        // Caught on the way down rather than on the way up, which is the whole
        // of whether this works: a TextBox that takes newlines handles Enter in
        // its own class handler and marks it dealt with, and a handler added the
        // ordinary way is never reached. Tunnelling gets there first.
        text.AddHandler(KeyDownEvent, Applied, RoutingStrategies.Tunnel);

        var apply = new Button
        {
            Content = "Apply  Ctrl+↵",
            Name = "apply",
            FontSize = 11,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(12, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        ToolTip.SetTip(apply, "Build this text and put the patch it describes on the canvas. "
            + "A text that does not read leaves whatever is playing alone.");

        apply.Click += (_, _) => EvaluateRequested?.Invoke(this, EventArgs.Empty);

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        Grid.SetColumn(footer, 0);
        Grid.SetColumn(apply, 1);
        bottom.Children.Add(footer);
        bottom.Children.Add(apply);

        var rows = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Background = new SolidColorBrush(Colors.Canvas),
        };

        Grid.SetRow(notice, 0);
        Grid.SetRow(text, 1);
        Grid.SetRow(complaintsScroll, 2);
        Grid.SetRow(bottom, 3);

        rows.Children.Add(notice);
        rows.Children.Add(text);
        rows.Children.Add(complaintsScroll);
        rows.Children.Add(bottom);

        Content = rows;
        Say(null);
    }

    private void Applied(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;

        e.Handled = true;
        EvaluateRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Somebody has asked for this text to become the patch.</summary>
    public event EventHandler? EvaluateRequested;

    /// <summary>The text as it stands, which is the document while this view owns it.</summary>
    public string Source
    {
        get => text.Text ?? string.Empty;
        set => text.Text = value;
    }

    /// <summary>
    /// What to say above the text about where it came from, or null when it is
    /// the document and there is nothing to warn about.
    /// </summary>
    public string? Notice
    {
        get => notice.Text;
        set
        {
            notice.Text = value;
            notice.IsVisible = !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>Whether the text may be typed into.</summary>
    public bool Editable
    {
        get => !text.IsReadOnly;
        set => text.IsReadOnly = !value;
    }

    public void Focus() => text.Focus();

    /// <summary>
    /// Shows what a build made of this text: every complaint against the line it
    /// is about, or a word about what was applied when there are none.
    /// </summary>
    /// <remarks>
    /// A failed build says so and changes nothing else. Whatever is playing goes
    /// on playing, which is the whole of why an evaluation is safe to try —
    /// there is no state here to be left half-built, because the language builds
    /// a patch or refuses to.
    /// </remarks>
    public void Show(LanguageLoad load, string? applied = null)
    {
        complaints.Children.Clear();

        foreach (var issue in load.Issues) complaints.Children.Add(Complaint(issue));

        complaintsScroll.IsVisible = load.Issues.Count > 0;

        Say(load.Ok
            ? applied
            : $"{load.Issues.Count} thing(s) to fix. The patch that was playing is untouched.");

        footer.Foreground = new SolidColorBrush(load.Ok ? Colors.Inactive : Colors.Attention);
    }

    /// <summary>Clears the complaints, for a view that has not been asked anything yet.</summary>
    public void Clear()
    {
        complaints.Children.Clear();
        complaintsScroll.IsVisible = false;
        Say(null);
        footer.Foreground = new SolidColorBrush(Colors.Inactive);
    }

    private void Say(string? said) =>
        footer.Text = said ?? "Ctrl+Enter builds this text and puts the patch on the canvas.";

    /// <summary>
    /// One complaint, which is a button because the useful thing to do with it
    /// is go there.
    /// </summary>
    private Control Complaint(LanguageIssue issue)
    {
        var row = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock
            {
                FontSize = 11.5,
                FontFamily = Mono,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.Attention),
                Text = $"{issue.Line}:{issue.Column}  {issue.Message}",
            },
        };

        ToolTip.SetTip(row, "Go to it.");

        row.Click += (_, _) =>
        {
            text.CaretIndex = Offset(Source, issue.Line, issue.Column);
            text.Focus();
        };

        return row;
    }

    /// <summary>
    /// Where a line and a column land in the text, counting from one as an
    /// editor does and clamped to the end for a complaint about a line that is
    /// no longer there.
    /// </summary>
    private static int Offset(string source, int line, int column)
    {
        var at = 0;

        for (var n = 1; n < line; n++)
        {
            var next = source.IndexOf('\n', at);

            if (next < 0) return source.Length;

            at = next + 1;
        }

        return Math.Min(at + Math.Max(column - 1, 0), source.Length);
    }
}
