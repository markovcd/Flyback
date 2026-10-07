using Avalonia.Input;
using Flyback.Engine.Graph;

namespace Flyback.Ui.Midi;

/// <summary>
/// Avalonia's keys by the names a browser gives the same physical keys, which is how
/// <see cref="ComputerKeyboard"/> knows them.
/// </summary>
internal static class KeyCodes
{
    /// <summary>The browser's name for <paramref name="key"/>: <c>KeyZ</c>, <c>Digit2</c>, <c>Comma</c>.</summary>
    public static string Of(Key key) => key switch
    {
        >= Key.A and <= Key.Z => $"Key{key}",
        >= Key.D0 and <= Key.D9 => $"Digit{key - Key.D0}",
        Key.OemComma => "Comma",
        Key.OemPeriod => "Period",
        Key.OemSemicolon => "Semicolon",
        Key.OemQuestion => "Slash",
        Key.OemQuotes => "Quote",
        Key.OemPipe => "Backslash",
        Key.OemOpenBrackets => "BracketLeft",
        Key.OemCloseBrackets => "BracketRight",
        _ => key.ToString(),
    };

    /// <summary>What note <paramref name="key"/> plays on <paramref name="keyboard"/>, or null where it plays none.</summary>
    public static int? Note(this ComputerKeyboard keyboard, Key key) => keyboard.Note(Of(key));
}
