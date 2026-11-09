using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Flyback.Engine.Language;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Hubs;

/// <summary>One word of the text view's completion list.</summary>
internal sealed class SourceCompletionItem(Completion offered, int order) : ICompletionData
{
    public Completion Offered => offered;

    public IImage? Image => null;

    public string Text => offered.Label;

    /// <summary>The word, and what it is in a quieter hand after it.</summary>
    public object Content => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = offered.Label },
            new TextBlock
            {
                Text = offered.Kind.ToString().ToLowerInvariant(),
                FontSize = Ui.Controls.Text.Small,
                Foreground = new SolidColorBrush(Colors.Inactive),
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };

    public object Description => $"{offered.Kind.ToString().ToLowerInvariant()}: {offered.Detail}";

    /// <summary>The engine's order, which puts what fits the place first.</summary>
    public double Priority => -order;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, offered.Insert);
}
