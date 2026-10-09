using Avalonia.Input;

namespace Flyback.Ui.Controls;

/// <summary>
/// The keys a picture with the screen answers, the same in the editor, its picture on
/// another monitor, and the viewer. Each asks here and does what it can of the answer.
/// </summary>
public static class StageKeys
{
    /// <summary>
    /// What <paramref name="key"/> means over the picture. Shift is no modifier here, as it
    /// is not for the computer's keyboard as an instrument.
    /// </summary>
    public static StageKey Read(Key key, KeyModifiers modifiers)
    {
        var command = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var bare = !command && (modifiers & KeyModifiers.Alt) == 0;

        return key switch
        {
            Key.Escape => StageKey.Leave,
            Key.F11 when bare => StageKey.FullScreen,
            Key.F3 when bare => StageKey.Stats,
            // Space, which no layout of the instrument plays, and the editor's Ctrl+P.
            Key.Space when bare => StageKey.Pause,
            Key.P when command => StageKey.Pause,
            _ => StageKey.None,
        };
    }
}
