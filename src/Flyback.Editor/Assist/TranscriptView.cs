using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Flyback.Ui.Controls;
using Flyback.Assist;
using Flyback.Plugins.Assist;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Assist;

/// <summary>The assistant's conversation as it is drawn, and as it is kept to be saved with the patch.</summary>
internal sealed class TranscriptView : ScrollViewer, ITranscript
{
    private static readonly IBrush Amber = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.Label);
    private static readonly IBrush Live = new ImmutableSolidColorBrush(Colors.Feedback);

    /// <summary>The person's side, in the Forms periwinkle: nothing else in the column is that color.</summary>
    private static readonly Color You = Colors.Form;

    private static readonly Color Bubble = Colors.Blend(Colors.Window, You, 0.16);

    /// <summary>How tall a long message stands before it is cut short behind "Show all".</summary>
    private const double Shortened = 132;

    /// <summary>How many lines a block may run to before it arrives folded.</summary>
    /// <remarks>
    /// Enough for a caption or a refusal to stand as it is, and short of what any
    /// tool answers with.
    /// </remarks>
    private const int FoldsOver = 8;

    /// <summary>How much of a folded block's first line its header shows.</summary>
    private const int Widest = 52;

    private readonly StackPanel saidPanel = new() { Spacing = 6, Margin = new Thickness(12, 12, 12, 8) };

    /// <summary>The transcript as shown, line by line, which is what is saved with the patch.</summary>
    private readonly List<TranscriptLine> lines = [];

    /// <summary>
    /// The paragraph the assistant is in the middle of, or null when it is not
    /// in the middle of one. Held rather than found, because what "the last
    /// block" is changes the moment anything else is written.
    /// </summary>
    private SelectableTextBlock? saying;

    /// <summary>The run of working the transcript is in the middle of, or null when the last thing was words.</summary>
    private StepsGroup? working;

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
        FontWeight = FontWeight.Medium,
        Foreground = Live,
        Margin = new Thickness(12, 0, 12, 12),
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
        working = null;
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

            // Not the steps' summary: a cost line says nothing about what was done.
            case Voice.Aside:
                Add(text, Text.Muted, Text.Small, work: true, gist: false);
                break;

            // The answer, however long it runs. Folding it would hide the one
            // thing the turn was for.
            case Voice.Proposed:
                Card(text, Colors.Feedback, Glyphs.Tick(10, Live), "Proposed patch", "Proposed: ");
                break;

            case Voice.Failed:
                Card(text, Colors.Attention, Glyphs.Warning(10, Amber), "Failed", null);
                break;

            case Voice.Briefing or Voice.Handbook:
                Add(text, Text.Muted, Text.Small);

                var block = saidPanel.Children[^1];
                block.IsVisible = !hidden.Contains(voice);
                hideable[voice].Add(block);
                break;

            default:
                Add(text, Text.Muted, Text.Small, work: true);
                break;
        }
    }

    /// <summary>
    /// One thing said, with the frame under it where it is one the assistant
    /// looked at. A heard sound is its caption alone: the WAV went to the model,
    /// and the patch under the cursor may already be playing.
    /// </summary>
    public void Put(Spoken spoken)
    {
        Put(spoken.Line.Voice, spoken.Line.Text, spoken.Keep);

        if (spoken.Event is PatchEvent.Saw saw) Picture(saw.Png);

        ScrollToEnd();
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

        Working().Add(new Border
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
        }, counted: false);
    }

    internal static string Tally(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Puts a block into the run of working, or at the top level, where it ends the run.</summary>
    private void Place(Control block, bool work, string? text = null)
    {
        if (work)
        {
            Working().Add(block, text: text);
            return;
        }

        Closed();
        saidPanel.Children.Add(block);
    }

    private StepsGroup Working()
    {
        if (working is null)
        {
            working = new StepsGroup();
            saidPanel.Children.Add(working);
        }

        return working;
    }

    private void Closed()
    {
        working?.Close();
        working = null;
    }

    private void Add(string text, IBrush color, double size, bool fold = true, bool work = false, bool gist = true)
    {
        // Anything else in the transcript ends the paragraph the assistant was
        // in the middle of. Without this, prose lands on the end of whatever
        // block happens to be last and of about the right size — which was the
        // person's own message, run together with the reply to it.
        saying = null;

        if (fold && Rows(text) > FoldsOver)
        {
            Fold(text, color, size, work, gist);
            return;
        }

        Place(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = color,
            FontSize = size,
        }, work, gist ? text : null);
    }

    /// <summary>
    /// A block that ends a turn, set apart in a card of its accent: what was
    /// proposed, or why it stopped.
    /// </summary>
    /// <param name="prefix">What the line opens with that the title already says, left off the body.</param>
    private void Card(string text, Color accent, Control mark, string title, string? prefix)
    {
        saying = null;
        Closed();

        var said = prefix is not null && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;

        var heading = Glyphs.Line(
            new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new ImmutableSolidColorBrush(Colors.Faded(accent, 0.16)),
                Child = mark,
            },
            new TextBlock { Text = title, FontSize = Text.Body, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });

        heading.Spacing = 8;

        var inside = new StackPanel { Spacing = 6 };

        inside.Children.Add(heading);
        inside.Children.Add(new SelectableTextBlock
        {
            Text = said,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ink,
            FontSize = Text.Body,
            LineHeight = 18,
        });

        saidPanel.Children.Add(new Border
        {
            Name = "card",
            Background = new ImmutableSolidColorBrush(Colors.Blend(Colors.Window, accent, 0.06)),
            BorderBrush = new ImmutableSolidColorBrush(Colors.Blend(Colors.Window, accent, 0.32)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 2, 0, 2),
            Child = inside,
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
    private void Fold(string text, IBrush color, double size, bool work, bool gist)
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

        Place(block, work, gist ? text : null);
    }

    /// <summary>
    /// The one line a folded block shows: which way it opens, what it was about,
    /// and how much of it there is.
    /// </summary>
    private static Control Gist(string text, bool open)
    {
        var first = text
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

        // The column is narrow and a button does not wrap, so what will not fit
        // is cut here rather than drawn past the edge of the panel.
        var gist = first.Length > Widest ? string.Concat(first.AsSpan(0, Widest - 1).TrimEnd(), "…") : first;

        var arrow = open ? Glyphs.Down(10) : Glyphs.Right(10);
        arrow.Name = open ? "open" : "folded";

        return Glyphs.Line(arrow, new TextBlock { Name = "gist", Text = $"{gist} · {Tally(Rows(text), "line")}" });
    }

    private static int Rows(string text) => text.AsSpan().Count('\n') + 1;

    /// <summary>
    /// What the person just asked for, in a bubble on the right, and the mark the
    /// assistant's side opens under.
    /// </summary>
    /// <remarks>
    /// A message long enough to push the reply off the column stands cut short,
    /// with a fade and a button to show the rest. The whole of it is drawn either
    /// way, so it can still be selected and read by anything walking the tree.
    /// </remarks>
    private void Asked(string text)
    {
        saying = null;
        Closed();

        var said = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            FontSize = Text.Body,
            LineHeight = 18,
        };

        var inside = new StackPanel { Spacing = 6 };

        if (Rows(text) > FoldsOver || text.Length > 480)
        {
            var fade = new Border
            {
                Height = 36,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.Faded(Bubble, 0), 0),
                        new GradientStop(Bubble, 1),
                    },
                },
            };

            var clipped = new Panel { Name = "message", MaxHeight = Shortened, ClipToBounds = true };

            clipped.Children.Add(said);
            clipped.Children.Add(fade);

            var more = new Button
            {
                Name = "more",
                FontSize = Text.Small,
                FontWeight = FontWeight.Medium,
                Foreground = new ImmutableSolidColorBrush(Colors.Blend(You, Avalonia.Media.Colors.White, 0.45)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 2),
                MinHeight = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            void Show()
            {
                var open = double.IsPositiveInfinity(clipped.MaxHeight);
                fade.IsVisible = !open;
                more.Content = Glyphs.Line(open ? Glyphs.Down(10) : Glyphs.Right(10), new TextBlock { Text = open ? "Show less" : "Show all" });
            }

            more.Click += (_, _) =>
            {
                clipped.MaxHeight = double.IsPositiveInfinity(clipped.MaxHeight) ? Shortened : double.PositiveInfinity;
                Show();
            };

            Show();
            inside.Children.Add(clipped);
            inside.Children.Add(more);
        }
        else
        {
            inside.Children.Add(said);
        }

        var turn = new StackPanel
        {
            Spacing = 5,
            Margin = new Thickness(28, saidPanel.Children.Count > 0 ? 18 : 0, 0, 6),
        };

        turn.Children.Add(new TextBlock
        {
            Text = "You",
            FontSize = Text.Small,
            FontWeight = FontWeight.SemiBold,
            Foreground = new ImmutableSolidColorBrush(Colors.Blend(You, Avalonia.Media.Colors.White, 0.35)),
            HorizontalAlignment = HorizontalAlignment.Right,
        });

        turn.Children.Add(new Border
        {
            Name = "you",
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new ImmutableSolidColorBrush(Bubble),
            BorderBrush = new ImmutableSolidColorBrush(Colors.Blend(Colors.Window, You, 0.4)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12, 3, 12, 12),
            Padding = new Thickness(12, 9),
            Child = inside,
        });

        saidPanel.Children.Add(turn);

        var mark = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(6),
            Background = new ImmutableSolidColorBrush(Colors.Faded(Colors.Feedback, 0.14)),
            Child = Glyphs.Spark(12, Live),
        };

        var assistant = Glyphs.Line(mark, new TextBlock
        {
            Text = "Assistant",
            FontSize = Text.Small,
            FontWeight = FontWeight.SemiBold,
            Foreground = new ImmutableSolidColorBrush(Colors.Blend(Colors.Feedback, Avalonia.Media.Colors.White, 0.3)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        assistant.Spacing = 7;
        assistant.Margin = new Thickness(0, 4, 0, 0);

        saidPanel.Children.Add(assistant);
    }

    /// <summary>Streamed prose arrives in pieces, so it lands on the end of the last one.</summary>
    private void Append(string text)
    {
        if (saying is not null)
        {
            saying.Text += text;
            return;
        }

        Closed();

        saying = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ink,
            FontSize = Text.Body,
            LineHeight = 18,
        };

        saidPanel.Children.Add(saying);
    }
}
