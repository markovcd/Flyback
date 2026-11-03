using Avalonia.Input;

namespace Flyback.Ui.Controls;

/// <summary>A double tap on a picture does the picture's one thing, such as taking the whole screen; a tap does nothing.</summary>
public static class PictureTaps
{
    /// <summary>Runs <paramref name="doubled"/> on a double tap of <paramref name="picture"/>.</summary>
    public static void Attach(InputElement picture, Action doubled) =>
        picture.DoubleTapped += (_, e) =>
        {
            doubled();
            e.Handled = true;
        };
}
