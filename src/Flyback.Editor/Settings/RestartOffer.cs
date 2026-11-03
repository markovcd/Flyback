using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Settings;

/// <summary>
/// The settings window's offer to start Flyback again, shown beside Save while the draft
/// holds a change only a restart puts in force, and naming it.
/// </summary>
/// <remarks>
/// Ticked whenever it appears, as the plugin window's is: the change does nothing until
/// Flyback starts again. Where Flyback cannot start itself again, which is iOS, a line
/// says how to instead.
/// </remarks>
internal sealed class RestartOffer : Panel
{
    private readonly CheckBox restart = new()
    {
        Name = "restart",
        IsChecked = true,
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    private readonly TextBlock note = new()
    {
        Name = "restartNote",
        FontSize = Text.Body,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    private readonly bool canRestart;

    private readonly bool saysHow;

    /// <param name="canRestart">Whether the host can start Flyback again.</param>
    /// <param name="saysHow">Whether to say how to restart by hand where it cannot.</param>
    public RestartOffer(bool canRestart, bool saysHow)
    {
        this.canRestart = canRestart;
        this.saysHow = saysHow;

        Children.Add(restart);
        Children.Add(note);
    }

    /// <summary>Whether Save should start Flyback again.</summary>
    public bool Asked => restart.IsVisible && restart.IsChecked == true;

    /// <summary>Offers the restart for <paramref name="what"/>, the end of "Restart Flyback to …", or takes the offer down for null.</summary>
    public void Offer(string? what)
    {
        var shown = what is not null && canRestart;

        if (shown && !restart.IsVisible) restart.IsChecked = true;

        restart.IsVisible = shown;
        restart.Content = $"Restart Flyback to {what}";

        note.IsVisible = what is not null && !canRestart && saysHow;
        note.Text = $"To {what}, swipe Flyback away in the app switcher and open it again.";

        IsVisible = restart.IsVisible || note.IsVisible;
    }

    /// <summary>"use the new colors" and "draw with Direct3D" as one phrase, or null for none.</summary>
    public static string? Joined(IEnumerable<string?> parts) =>
        parts.OfType<string>().ToList() switch
        {
            [] => null,
            [var one] => one,
            var many => string.Join(", ", many[..^1]) + " and " + many[^1],
        };
}
