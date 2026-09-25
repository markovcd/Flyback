using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Flyback.App.Controls;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Assist;

/// <summary>The assistant's conversation as it is drawn, and as it is kept to be saved with the patch.</summary>
internal sealed class TranscriptView : ScrollViewer
{
    private static readonly IBrush Amber = new SolidColorBrush(Colors.Attention);

    /// <summary>How many lines a block may run to before it arrives folded.</summary>
    /// <remarks>
    /// Enough for a caption or a refusal to stand as it is, and short of what any
    /// tool answers with.
    /// </remarks>
    private const int FoldsOver = 8;

    /// <summary>How much of a folded block's first line its header shows.</summary>
    private const int Widest = 52;

    private readonly StackPanel saidPanel = new() { Spacing = 4, Margin = new Thickness(10, 8) };

    /// <summary>The transcript as shown, line by line, which is what is saved with the patch.</summary>
    private readonly List<TranscriptLine> lines = [];

    /// <summary>
    /// The paragraph the assistant is in the middle of, or null when it is not
    /// in the middle of one. Held rather than found, because what "the last
    /// block" is changes the moment anything else is written.
    /// </summary>
    private SelectableTextBlock? saying;

    /// <summary>The blocks drawn for each voice <see cref="Shows"/> can hide, and the voices hidden now.</summary>
    private readonly Dictionary<Voice, List<Control>> hideable = new()
    {
        [Voice.Briefing] = [],
        [Voice.Handbook] = [],
    };

    private readonly HashSet<Voice> hidden = [];

    public TranscriptView()
    {
        // The live line below the transcript rather than in it, so nothing has to
        // move it back to the end every time something is written.
        var flow = new StackPanel();

        flow.Children.Add(saidPanel);
        flow.Children.Add(Thinking);

        Content = flow;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>
    /// That the assistant has the turn, at the end of the transcript where the
    /// next thing it says will appear.
    /// </summary>
    /// <remarks>
    /// The beacon above says a run is alive; this says where. Minutes can pass
    /// between one paragraph and the next, and the eye is on the conversation
    /// rather than on the header by then.
    /// </remarks>
    public TextBlock Thinking { get; } = new()
    {
        Name = "thinking",
        FontSize = Text.Body,
        Foreground = new SolidColorBrush(Colors.Feedback),
        Margin = new Thickness(10, 0, 10, 8),
        IsVisible = false,
    };

    /// <summary>
    /// Whether the <see cref="Voice.Briefing"/> or <see cref="Voice.Handbook"/> lines
    /// are drawn. Kept either way, so turning one back on shows all of it.
    /// </summary>
    public void Shows(Voice voice, bool shown)
    {
        if (shown) hidden.Remove(voice);
        else hidden.Add(voice);

        foreach (var block in hideable[voice]) block.IsVisible = shown;
    }

    /// <summary>The lines that are saved with the patch.</summary>
    public IReadOnlyList<TranscriptLine> Lines => lines;

    /// <summary>Whether anything at all is drawn, kept or not.</summary>
    public bool IsEmpty => saidPanel.Children.Count == 0;

    public void Clear()
    {
        saidPanel.Children.Clear();
        lines.Clear();
        foreach (var blocks in hideable.Values) blocks.Clear();
        saying = null;
    }

    /// <summary>
    /// One line of the transcript, drawn as its voice is drawn and kept, so that it
    /// is saved with the patch — unless it is only about this showing of it.
    /// </summary>
    public void Put(Voice voice, string text, bool keep = true)
    {
        if (keep) lines.Add(new TranscriptLine(voice, text));

        switch (voice)
        {
            case Voice.You:
                Asked(text);
                break;

            case Voice.Said:
                Append(text);
                break;

            case Voice.Aside:
                Add(text, Text.Muted, 11);
                break;

            // The answer, however long it runs. Folding it would hide the one
            // thing the turn was for.
            case Voice.Proposed:
                Add(text, Brushes.White, Text.Body, fold: false);
                break;

            case Voice.Failed:
                Add(text, Amber, Text.Small);
                break;

            case Voice.Briefing or Voice.Handbook:
                Add(text, Text.Muted, Text.Small);

                var block = saidPanel.Children[^1];
                block.IsVisible = !hidden.Contains(voice);
                hideable[voice].Add(block);
                break;

            default:
                Add(text, Text.Muted, Text.Small);
                break;
        }
    }

    /// <summary>
    /// A frame the assistant looked at, in the transcript under the caption that
    /// came with it.
    /// </summary>
    /// <remarks>
    /// Full width and capped in height, because a render is a contact sheet: one
    /// frame is a picture and four are a strip, and both have to be readable
    /// without either taking the transcript over.
    /// </remarks>
    public void Picture(byte[] png)
    {
        Bitmap frame;

        try
        {
            frame = new Bitmap(new MemoryStream(png));
        }
        catch
        {
            // A frame that will not decode is nothing to show. The caption that
            // came with it is already in the transcript.
            return;
        }

        saying = null;

        saidPanel.Children.Add(new Border
        {
            Name = "frame",
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 4),

            // Sized by the picture rather than by the panel, so a single frame
            // does not sit in a box with bars either side of it.
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Image
            {
                Source = frame,
                Stretch = Stretch.Uniform,
                MaxHeight = 260,
            },
        });
    }

    internal static string Tally(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private void Add(string text, IBrush color, double size, bool fold = true)
    {
        // Anything else in the transcript ends the paragraph the assistant was
        // in the middle of. Without this, prose lands on the end of whatever
        // block happens to be last and of about the right size — which was the
        // person's own message, run together with the reply to it.
        saying = null;

        if (fold && Rows(text) > FoldsOver)
        {
            Fold(text, color, size);
            return;
        }

        saidPanel.Children.Add(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = color,
            FontSize = size,
        });
    }

    /// <summary>
    /// A block too long to read on the way past, behind its first line and a
    /// count of the rest.
    /// </summary>
    /// <remarks>
    /// What a tool answered is usually the patch written out, which is screens of
    /// text between one thing the assistant said and the next. Folded, the
    /// transcript is the conversation again, and the working is a click away.
    /// </remarks>
    private void Fold(string text, IBrush color, double size)
    {
        var body = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = color,
            FontSize = size,
            Margin = new Thickness(11, 1, 0, 3),
            IsVisible = false,
        };

        var header = new Button
        {
            Name = "fold",
            Content = Gist(text, open: false),
            FontSize = size,
            Foreground = color,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 1),
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        header.Click += (_, _) =>
        {
            body.IsVisible = !body.IsVisible;
            header.Content = Gist(text, body.IsVisible);
        };

        var block = new StackPanel();

        block.Children.Add(header);
        block.Children.Add(body);

        saidPanel.Children.Add(block);
    }

    /// <summary>
    /// The one line a folded block shows: which way it opens, what it was about,
    /// and how much of it there is.
    /// </summary>
    private static string Gist(string text, bool open)
    {
        var first = text
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

        // The column is narrow and a button does not wrap, so what will not fit
        // is cut here rather than drawn past the edge of the panel.
        var gist = first.Length > Widest ? string.Concat(first.AsSpan(0, Widest - 1).TrimEnd(), "…") : first;

        return $"{(open ? "▾" : "▸")} {gist} · {Tally(Rows(text), "line")}";
    }

    private static int Rows(string text) => text.AsSpan().Count('\n') + 1;

    /// <summary>
    /// What the person just asked for, set apart from what comes back.
    /// </summary>
    /// <remarks>
    /// A conversation is kept now rather than cleared per message, so the two
    /// sides have to be told apart by looking: a gap above, and the accent the
    /// rest of the panel uses for its own voice.
    /// </remarks>
    private void Asked(string text)
    {
        saying = null;

        saidPanel.Children.Add(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = Text.Body,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, saidPanel.Children.Count > 0 ? 14 : 0, 0, 2),
        });
    }

    /// <summary>Streamed prose arrives in pieces, so it lands on the end of the last one.</summary>
    private void Append(string text)
    {
        if (saying is not null)
        {
            saying.Text += text;
            return;
        }

        saying = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = Text.Body,
        };

        saidPanel.Children.Add(saying);
    }
}
