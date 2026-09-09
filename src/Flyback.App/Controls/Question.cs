using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// Something asked in place, with a tick and a cross docked to the right of it.
/// </summary>
/// <remarks>
/// A row rather than a dialog, because what is being asked about is on the
/// screen and a dialog would cover it. The question replaces the thing it is
/// about — the row a module was on, the entry being removed — so the answer is
/// given where the thing stands rather than in front of it, and nothing has to
/// be remembered across a modal.
/// <para>
/// The cross is dimmer than the tick, which is the one piece of styling here
/// that means something: the two are not equal weights. Saying no is the
/// default the row already had before it asked, so it is offered rather than
/// urged.
/// </para>
/// <para>
/// Shared because the inspector and the module list ask in exactly the same
/// shape, and had said so in two places at slightly different sizes. What a
/// caller still chooses is the wording, the tips and where the row sits; how a
/// question looks is not a thing two places should be free to disagree about.
/// </para>
/// </remarks>
internal static class Question
{
    /// <param name="margin">Where the row sits in whatever is holding it.</param>
    /// <param name="answered">
    /// True for the tick and false for the cross. Always called exactly once,
    /// so a caller may take the row down in it without asking which was pressed.
    /// </param>
    public static Control Row(
        string question,
        Thickness margin,
        string yesTip,
        string noTip,
        Action<bool> answered)
    {
        var row = new DockPanel { Margin = margin };

        var answers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };

        answers.Children.Add(Answer("✔", yesTip, 1, () => answered(true)));
        answers.Children.Add(Answer("✕", noTip, 0.55, () => answered(false)));

        DockPanel.SetDock(answers, Dock.Right);
        row.Children.Add(answers);

        row.Children.Add(new TextBlock
        {
            Text = question,
            FontSize = Text.Body,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });

        return row;

        static Button Answer(string glyph, string tip, double strength, Action taken)
        {
            var button = new Button
            {
                Content = glyph,
                FontSize = Text.Small,
                Padding = new Thickness(6, 2),
                Background = Brushes.Transparent,
                Opacity = strength,
                VerticalAlignment = VerticalAlignment.Center,
            };

            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => taken();

            return button;
        }
    }
}
