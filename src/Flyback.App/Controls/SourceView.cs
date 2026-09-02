using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Rendering;
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
/// An editor rather than a text box, and it is the one place in the shell that
/// takes a package to do its job. A gutter to mark the line a complaint is
/// about, and colour to tell a module from the socket it is being handed, are
/// what a surface somebody types a patch into <em>while it plays</em> needs, and
/// a <see cref="TextBox"/> has no rich text in it at all. Avalonia's own
/// RichTextEditor is a word processor — no highlighting, no line numbers, and a
/// Pro licence — so this is AvalonEdit, which the Avalonia organisation ports
/// and publishes under the same licence this program carries.
/// </para>
/// </remarks>
internal sealed class SourceView : UserControl
{
    private static readonly FontFamily Mono =
        new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>
    /// The language, coloured — see <c>Flyback.xshd</c> beside this file.
    /// </summary>
    /// <remarks>
    /// Loaded once and shared, because a highlighting definition is immutable
    /// and reading the same XML per editor would be work done for nothing. Null
    /// if it will not load at all, which leaves plain text rather than taking
    /// the window down over a colour scheme.
    /// </remarks>
    private static readonly IHighlightingDefinition? Language = LoadHighlighting();

    private readonly TextEditor text = new()
    {
        Name = "source",
        FontFamily = Mono,
        FontSize = 13,
        ShowLineNumbers = true,
        WordWrap = false,
        Background = new SolidColorBrush(Colors.Canvas),
        Foreground = new SolidColorBrush(Colors.Label),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(6, 8),
        SyntaxHighlighting = Language,
    };

    /// <summary>The lines a build complained about, drawn behind the text.</summary>
    private readonly Complaints marked = new();

    /// <summary>The statement the caret was last in, so a move within one says nothing.</summary>
    private string reading = string.Empty;

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

        text.TextArea.TextView.BackgroundRenderers.Add(marked);
        text.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Colors.Feedback);

        // The gutter and the line the caret is on, in the shell's own colours
        // rather than the theme's: this sits where the canvas sits and should
        // not read as a different program.
        text.LineNumbersForeground = new SolidColorBrush(Colors.Inactive);
        text.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Colors.Node);
        text.TextArea.TextView.CurrentLineBorder = null;
        text.Options.HighlightCurrentLine = true;
        text.Options.ConvertTabsToSpaces = true;
        text.Options.IndentationSize = 2;

        // Enter belongs to the editor, so the gesture that applies a patch has
        // to be one the editor does not want. Ctrl+Enter is what every live
        // coding environment uses for exactly this, and it is free here for the
        // same reason it is free there.
        //
        // Caught on the way down rather than on the way up, which is the whole
        // of whether this works: an editor that takes newlines handles Enter in
        // its own class handler and marks it dealt with, and a handler added the
        // ordinary way is never reached. Tunnelling gets there first.
        text.AddHandler(KeyDownEvent, Applied, RoutingStrategies.Tunnel);

        // What the toolbar's undo and redo may do changes with every keystroke,
        // and nothing else here would notice.
        text.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        // Only when the statement under the caret changes. Moving along one line
        // is still the same module, and telling the inspector otherwise would
        // rebuild it on every arrow key.
        text.TextArea.Caret.PositionChanged += (_, _) =>
        {
            var statement = Statement();

            if (statement == reading) return;

            reading = statement;
            Reading?.Invoke(this, statement);
        };

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

    /// <summary>The text has changed, so what can be taken back has too.</summary>
    public event EventHandler? Changed;

    /// <summary>The caret has moved to another statement.</summary>
    /// <remarks>
    /// Another <em>statement</em> rather than another character: what listens is
    /// the inspector, and rebuilding it for every keystroke along one line would
    /// be a panel that flickered while somebody typed.
    /// </remarks>
    public event EventHandler<string>? Reading;

    /// <summary>
    /// The statement the caret is in, as text — the line it is on, or the
    /// nearest line above that begins one.
    /// </summary>
    /// <remarks>
    /// A statement may be several lines: a pipeline broken before each stage
    /// puts the caret on `|> gain(0.5)`, which names nothing. What names
    /// something is the line the statement began on, so this walks up until it
    /// finds one that is not a continuation.
    /// </remarks>
    public string Statement()
    {
        var document = text.Document;

        if (document is null || document.LineCount == 0) return string.Empty;

        for (var number = Math.Clamp(text.TextArea.Caret.Line, 1, document.LineCount); number >= 1; number--)
        {
            var line = document.GetText(document.GetLineByNumber(number)).TrimStart();

            if (line.Length == 0) return string.Empty;
            if (!Continues(line)) return line;
        }

        return string.Empty;
    }

    /// <summary>Whether a line is the middle of a statement rather than the start of one.</summary>
    private static bool Continues(string line) =>
        line.StartsWith("|>", StringComparison.Ordinal)
        || line.StartsWith(')') || line.StartsWith(']')
        || line.StartsWith('}');

    /// <summary>
    /// Sets one knob in the text, as the one statement the language has for
    /// saying so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replacing the line that already sets it where there is one, and adding
    /// one at the end where there is not. At the end because a knob may be set
    /// anywhere after the module is declared, and the end is the one place that
    /// is true of without reading the rest.
    /// </para>
    /// <para>
    /// Through the document rather than by assigning the text, so it is an edit
    /// that Ctrl+Z takes back and not a document that empties the stack.
    /// </para>
    /// </remarks>
    public void Set(string module, string port, string value)
    {
        var document = text.Document;

        if (document is null) return;

        var wanted = $"{module}.{port} = {value}";
        var head = $"{module}.{port} ";

        for (var number = 1; number <= document.LineCount; number++)
        {
            var line = document.GetLineByNumber(number);
            var said = document.GetText(line);

            if (!said.TrimStart().StartsWith(head, StringComparison.Ordinal)) continue;
            if (said == wanted) return;

            document.Replace(line.Offset, line.Length, wanted);
            return;
        }

        var end = document.TextLength;
        var before = end > 0 && document.GetCharAt(end - 1) != '\n' ? "\n" : string.Empty;

        document.Insert(end, before + wanted + "\n");
    }

    public bool CanUndo => text.CanUndo;

    public bool CanRedo => text.CanRedo;

    public void Undo() => text.Undo();

    public void Redo() => text.Redo();

    /// <summary>
    /// Folds the long lines, which is what laying the modules out is on the
    /// other side of the switch.
    /// </summary>
    /// <remarks>
    /// One step on the undo stack rather than however many the editor would
    /// make of a whole-document replacement, and the caret is put back where it
    /// was — tidying is a thing done to what somebody is reading, and losing
    /// their place in it is the one way to make it not worth doing.
    /// </remarks>
    public void Tidy()
    {
        var folded = SourceLayout.Wrap(Source);

        if (folded == Source) return;

        var line = text.TextArea.Caret.Line;
        var column = text.TextArea.Caret.Column;

        text.Document.BeginUpdate();
        text.Document.Text = folded;
        text.Document.EndUpdate();

        text.TextArea.Caret.Line = Math.Clamp(line, 1, text.Document.LineCount);
        text.TextArea.Caret.Column = Math.Max(column, 1);
        text.TextArea.Caret.BringCaretToView();
    }

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

    public void Focus() => text.TextArea.Focus();

    /// <summary>
    /// Shows what a build made of this text: every complaint against the line it
    /// is about, in the gutter and in a list under it, or a word about what was
    /// applied when there are none.
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

        marked.Lines = [.. load.Issues.Select(issue => issue.Line)];
        text.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

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

        marked.Lines = [];
        text.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

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

        row.Click += (_, _) => GoTo(issue);

        return row;
    }

    /// <summary>
    /// Puts the caret where a complaint is about, clamped to a document that may
    /// have been typed into since the build that produced it.
    /// </summary>
    private void GoTo(LanguageIssue issue)
    {
        var document = text.Document;

        if (document is null || document.LineCount == 0) return;

        var line = document.GetLineByNumber(Math.Clamp(issue.Line, 1, document.LineCount));
        var column = Math.Clamp(issue.Column, 1, line.Length + 1);

        text.TextArea.Caret.Line = line.LineNumber;
        text.TextArea.Caret.Column = column;
        text.TextArea.Caret.BringCaretToView();
        text.TextArea.Focus();
    }

    /// <summary>The language's colours, or null where they would not load.</summary>
    private static IHighlightingDefinition? LoadHighlighting()
    {
        try
        {
            using var stream = typeof(SourceView).Assembly
                .GetManifestResourceStream("Flyback.App.Controls.Flyback.xshd");

            if (stream is null) return null;

            using var reader = XmlReader.Create(stream);

            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception)
        {
            // Plain text rather than no window. Nothing here is load-bearing:
            // the language reads the same either way.
            return null;
        }
    }

    /// <summary>
    /// Paints the lines a build complained about.
    /// </summary>
    /// <remarks>
    /// A background rather than a squiggle under the exact column, and the
    /// reason is that a complaint is usually about a line: one mistake stops a
    /// statement being read, so what follows is a run of complaints about names
    /// that statement was going to make. A whole line lit is the honest width of
    /// that, and the column is still in the list underneath.
    /// </remarks>
    private sealed class Complaints : IBackgroundRenderer
    {
        private readonly IBrush wash = new SolidColorBrush(Colors.Sink, 0.16);

        public IReadOnlyList<int> Lines { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView view, DrawingContext context)
        {
            if (Lines.Count == 0 || view.Document is null) return;

            view.EnsureVisualLines();

            foreach (var number in Lines)
            {
                if (number < 1 || number > view.Document.LineCount) continue;

                var line = view.Document.GetLineByNumber(number);

                foreach (var piece in BackgroundGeometryBuilder.GetRectsForSegment(view, line))
                    context.FillRectangle(wash, new Rect(piece.X, piece.Y, view.Bounds.Width, piece.Height));
            }
        }
    }
}
