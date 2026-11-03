using Avalonia.Input;

namespace Flyback.Ui.Controls;

/// <summary>A tap on a picture plays and pauses, and a double tap does the picture's other thing.</summary>
public static class PictureTaps
{
    /// <summary>
    /// Pauses or plays on a tap of <paramref name="picture"/> while <paramref name="pauses"/> says so,
    /// always when it is left out. A double tap arrives as a tap and then a double tap, so the second
    /// puts the first back before running <paramref name="doubled"/>.
    /// </summary>
    public static void Attach(InputElement picture, Action toggle, Action? doubled = null, Func<bool>? pauses = null)
    {
        picture.Tapped += (_, e) =>
        {
            if (pauses?.Invoke() != false) toggle();
            e.Handled = true;
        };

        picture.DoubleTapped += (_, e) =>
        {
            if (pauses?.Invoke() != false) toggle();
            doubled?.Invoke();
            e.Handled = true;
        };
    }
}
