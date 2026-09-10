using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Rendering;
using Flyback.Core.Language;

namespace Flyback.App.Controls;

/// <summary>
/// The patch as text, and the one gesture that turns it into the patch.
/// </summary>
/// <remarks>
/// It shows and it asks; it does not build. Whether a source reads is the
/// shell's business — this raises <see cref="EvaluateRequested"/> and is handed a
/// <see cref="LanguageLoad"/> to show — which is what lets it be tested without a
/// compiler.
/// <para>
/// The one place in the shell that takes a package: a gutter for the line a
/// complaint is about and color to tell a module from a socket are what a surface
/// somebody types a patch into while it plays needs, and a
/// <see cref="TextBox"/> has no rich text at all. Avalonia's own RichTextEditor
/// is a word processor with a Pro licence, so this is AvalonEdit.
/// </para>
/// </remarks>
internal sealed class SourceView : UserControl
{
    private static readonly FontFamily Mono =
        new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>
    /// The language, colored — see <c>Flyback.xshd</c> beside this file. Loaded
    /// once and shared, since a highlighting definition is immutable. Null if it
    /// will not load, which leaves plain text rather than taking the window down
    /// over a color scheme.
    /// </summary>
    private static readonly IHighlightingDefinition? Language = LoadHighlighting();

    private readonly TextEditor text = new()
    {
        Name = "source",
        FontFamily = Mono,
        FontSize = Text.Emphasis,
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

    /// <summary>
    /// What the shell has to say about who owns this text, shown above it — the
    /// one place a person is told that what they are looking at is a reading
    /// rather than the patch (ADR-0068). Above the text rather than in the status
    /// bar, because it is a fact about this text.
    /// </summary>
    private readonly TextBlock notice = new()
    {
        FontSize = Text.Small,
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
        FontSize = Text.Small,
        Margin = new Thickness(12, 5),
        Foreground = new SolidColorBrush(Colors.Inactive),
    };

    /// <summary>
    /// The counterpart of Apply: the gesture that gives the patch back to the
    /// canvas. Shown only while the text is the document — over a printing there
    /// is nothing to hand back.
    /// </summary>
    private readonly Button hand = new()
    {
        Content = "Edit on the canvas",
        Name = "hand",
        FontSize = Text.Small,
        Padding = new Thickness(10, 4),
        Margin = new Thickness(0, 5),
        IsVisible = false,
    };

    /// <summary>
    /// Whether a character is being typed at this instant — whether, that is,
    /// the change and the caret move about to happen are that character's rather
    /// than somebody else's.
    /// </summary>
    private bool typing;

    /// <summary>Whether the next character typed joins the step the last one made.</summary>
    private bool joining;

    public SourceView()
    {
        complaintsScroll.Content = complaints;

        text.TextArea.TextView.BackgroundRenderers.Add(marked);
        text.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Colors.Feedback);

        // The gutter and the line the caret is on, in the shell's own colors
        // rather than the theme's: this sits where the canvas sits and should
        // not read as a different program.
        text.LineNumbersForeground = new SolidColorBrush(Colors.Inactive);
        text.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Colors.Node);
        text.TextArea.TextView.CurrentLineBorder = null;
        text.Options.HighlightCurrentLine = true;
        text.Options.ConvertTabsToSpaces = true;
        text.Options.IndentationSize = 2;

        // Enter belongs to the editor, so the gesture that applies a patch has to
        // be one the editor does not want; Ctrl+Enter is what every live coding
        // environment uses for this.
        //
        // Caught on the way down rather than up, which is the whole of whether it
        // works: an editor that takes newlines marks Enter dealt with in its own
        // class handler, and a handler added the ordinary way is never reached.
        text.AddHandler(KeyDownEvent, Applied, RoutingStrategies.Tunnel);

        // A run of typing is one thing to take back. The stack takes an operation
        // per change and a change is a keystroke, so without this a sentence comes
        // back a letter at a time.
        //
        // Caught either side of the keystroke, because the editor makes the change
        // in between: a group left open across two keystrokes would be one no undo
        // could be pressed inside.
        text.TextArea.AddHandler(TextInputEvent, Typing, RoutingStrategies.Tunnel);
        text.AddHandler(TextInputEvent, Typed, RoutingStrategies.Bubble, handledEventsToo: true);

        // What the toolbar's undo and redo may do changes with every keystroke,
        // and nothing else here would notice. And anything that moved the text
        // other than a keystroke ends the run — a deletion, an undo, a value
        // written back, a document loaded — said by watching rather than by
        // listing them, so a way of changing the text nobody thought of here
        // ends the run rather than quietly joining it.
        text.TextChanged += (_, _) =>
        {
            if (!typing) joining = false;

            Changed?.Invoke(this, EventArgs.Empty);
        };

        // Every move, because what listens works in offsets and a statement is
        // no longer the unit: four modules can share one line, and the caret
        // stepping from one to the next is a different module each time. A move
        // that is not a keystroke's ends the run of typing besides: somebody who
        // clicked somewhere else is writing somewhere else.
        text.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (!typing) joining = false;

            Moved?.Invoke(this, Caret);
        };

        var apply = new Button
        {
            Content = "Apply  Ctrl+↵",
            Name = "apply",
            FontSize = Text.Small,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(12, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        ToolTip.SetTip(apply, "Build this text and put the patch it describes on the canvas. "
            + "A text that does not read leaves whatever is playing alone.");

        apply.Click += (_, _) => EvaluateRequested?.Invoke(this, EventArgs.Empty);

        ToolTip.SetTip(hand, "Give the patch back to the canvas, so its modules can be dragged, "
            + "wired and grouped again. The text stops being the document, so save it first if "
            + "what is written here is worth keeping.");

        hand.Click += (_, _) => HandBackRequested?.Invoke(this, EventArgs.Empty);

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

        Grid.SetColumn(footer, 0);
        Grid.SetColumn(hand, 1);
        Grid.SetColumn(apply, 2);
        bottom.Children.Add(footer);
        bottom.Children.Add(hand);
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

    /// <summary>
    /// A character is on its way in: it joins the step the character before it
    /// made, or begins one of its own. A word is the unit and the space that ends
    /// one belongs to it, so whitespace joins the run and then ends it.
    /// </summary>
    private void Typing(object? sender, TextInputEventArgs e)
    {
        if (text.Document is not { } document) return;

        typing = true;

        if (joining) document.UndoStack.StartContinuedUndoGroup();
        else document.UndoStack.StartUndoGroup();

        joining = !string.IsNullOrWhiteSpace(e.Text);
    }

    /// <summary>The character is in, so the step it belongs to is closed again.</summary>
    /// <remarks>
    /// <see cref="typing"/> says whether there is a group to close: an input that
    /// arrived before there was a document to put it in opened none.
    /// </remarks>
    private void Typed(object? sender, TextInputEventArgs e)
    {
        if (!typing) return;

        typing = false;
        text.Document?.UndoStack.EndUndoGroup();
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

    /// <summary>Somebody has asked for the canvas to have the patch back.</summary>
    /// <remarks>
    /// Asked rather than done, for the reason applying is asked rather than
    /// done: what it settles is which of two views is the document, and that is
    /// the shell's to answer — and to ask about first, since the text is about
    /// to stop being anywhere.
    /// </remarks>
    public event EventHandler? HandBackRequested;

    /// <summary>The text has changed, so what can be taken back has too.</summary>
    public event EventHandler? Changed;

    /// <summary>The caret has moved, carrying where in the text it now is.</summary>
    /// <remarks>
    /// What listens is the inspector, by way of the map that says which module
    /// the text at an offset is. An offset rather than a line, because a line is
    /// not the unit: <c>mul(a: sine(), b: saw())</c> is three modules on one of
    /// them and the caret picks between them by where it stands.
    /// </remarks>
    public event EventHandler<int>? Moved;

    /// <summary>Where the caret is, as an offset into the text.</summary>
    /// <remarks>
    /// Settable so that text replaced wholesale can put somebody back where
    /// they were reading. Moving it is a move like any other, so the panel
    /// follows it exactly as it follows an arrow key.
    /// </remarks>
    public int Caret
    {
        get => text.TextArea.Caret.Offset;
        set => text.TextArea.Caret.Offset = value;
    }

    /// <summary>
    /// Puts <paramref name="change"/> into the text, leaving everything else
    /// exactly as it was.
    /// </summary>
    /// <remarks>
    /// Through the document rather than by assigning the text: a document replaced
    /// empties the undo stack, moves the caret to the top and scrolls away from
    /// whatever somebody was reading.
    /// </remarks>
    /// <returns>Whether the text now says something it did not say before.</returns>
    public bool Apply(Change change)
    {
        if (text.Document is not { } document) return false;
        if (change.Offset < 0 || change.Offset + change.Length > document.TextLength) return false;

        // A knob put back where it already was is not an edit, and recording one
        // would put a step on the undo stack that undoes nothing.
        if (document.GetText(change.Offset, change.Length) == change.Text) return false;

        document.Replace(change.Offset, change.Length, change.Text);

        return true;
    }

    public bool CanUndo => text.CanUndo;

    public bool CanRedo => text.CanRedo;

    public void Undo() => text.Undo();

    public void Redo() => text.Redo();

    /// <summary>
    /// Something that is not a text edit, put on this stack so that taking back
    /// what is showing takes it back too.
    /// </summary>
    /// <param name="undone">Called to put it back as it was.</param>
    /// <param name="redone">Called to do it again.</param>
    private sealed class Deed(Action undone, Action redone) : IUndoableOperation
    {
        public void Undo() => undone();

        public void Redo() => redone();
    }

    /// <summary>
    /// Puts something that is not a text edit on the undo stack, to be taken back
    /// in its turn.
    /// </summary>
    /// <remarks>
    /// The stack the editor already keeps rather than a second one beside it,
    /// because what has to be right is the order: typing, applying and turning a
    /// knob are one run of things somebody did, and two stacks would have to be
    /// interleaved by guessing.
    /// </remarks>
    public void Remember(Action undone, Action redone)
    {
        // And typing does not join it. What goes on the stack here is not a
        // character somebody typed, and the next one that is starts a step of
        // its own rather than folding a knob into a word.
        joining = false;

        text.Document?.UndoStack.Push(new Deed(undone, redone));
    }

    /// <summary>
    /// Makes everything done before this is disposed one thing to take back,
    /// however many edits and deeds it turns out to be.
    /// </summary>
    public IDisposable Together() => new Group(text.Document?.UndoStack);

    /// <summary>
    /// Says that nothing written here so far is a thing to take back.
    /// </summary>
    /// <remarks>
    /// For text that is a reading rather than a document: what the caller wrote, it
    /// wrote to keep the reading true. Left on the stack, taking it back would
    /// leave the reading saying one thing and the patch another.
    /// </remarks>
    public void ForgetSteps() => text.Document?.UndoStack.ClearAll();

    /// <summary>One undo group, opened and closed with a <c>using</c>.</summary>
    private sealed class Group : IDisposable
    {
        private readonly UndoStack? stack;

        public Group(UndoStack? stack)
        {
            this.stack = stack;
            stack?.StartUndoGroup();
        }

        public void Dispose() => stack?.EndUndoGroup();
    }

    /// <summary>
    /// Folds the long lines, which is what laying the modules out is on the other
    /// side of the switch. One step on the undo stack rather than however many a
    /// whole-document replacement would make, and the caret is put back where it
    /// was.
    /// </summary>
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

    /// <summary>
    /// Whether this text is the document, which is the one state in which
    /// handing the patch back to the canvas means anything.
    /// </summary>
    public bool Owns
    {
        get => hand.IsVisible;
        set => hand.IsVisible = value;
    }

    /// <summary>Whether the text may be typed into.</summary>
    public bool Editable
    {
        get => !text.IsReadOnly;
        set => text.IsReadOnly = !value;
    }

    public void Focus() => text.TextArea.Focus();

    /// <summary>
    /// Shows what a build made of this text: every complaint against the line it is
    /// about, in the gutter and in a list under it, or a word about what was
    /// applied when there are none.
    /// </summary>
    /// <remarks>
    /// A failed build changes nothing else and whatever is playing goes on playing:
    /// the language builds a patch or refuses to, so there is no half-built state
    /// to be left in.
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
                FontSize = Text.Body,
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

    /// <summary>The language's colors, or null where they would not load.</summary>
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
    /// A background rather than a squiggle under the exact column, because a
    /// complaint is usually about a line: one mistake stops a statement being read,
    /// and what follows is a run of complaints about names it was going to make.
    /// The column is still in the list underneath.
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
