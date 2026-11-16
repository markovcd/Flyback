using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace Flyback.Editor.Hubs;

/// <summary>
/// Copy, cut, paste, select all, undo and redo in the text, on the platform's own command key.
/// </summary>
/// <remarks>
/// The text editor binds those commands to Ctrl everywhere but a macOS process, so in a
/// browser on a Mac it ignores Cmd+C. The platform's hotkey configuration knows the
/// browser is on a Mac, and where its command key is not the editor's, the keys are run here.
/// </remarks>
internal static class SourceCommandKeys
{
    /// <summary>Catches the command keys on <paramref name="editor"/>, with the key its platform names at each press.</summary>
    public static void Attach(TextEditor editor) =>
        Attach(editor, () => editor.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers);

    /// <summary>Catches the command keys on <paramref name="editor"/>, with <paramref name="command"/> as the command key.</summary>
    public static void Attach(TextEditor editor, Func<KeyModifiers?> command) =>
        editor.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (command() is not { } held || held == ApplicationCommands.Copy.Gesture.KeyModifiers) return;
            if (Meant(e, held) is not { } meant) return;

            // As the editor's own binding does: a command with nothing to act on leaves the key to the window.
            if (!meant.CanExecute(null, editor.TextArea)) return;

            e.Handled = true;
            meant.Execute(null, editor.TextArea);
        }, RoutingStrategies.Tunnel);

    private static RoutedCommand? Meant(KeyEventArgs e, KeyModifiers command) =>
        e.KeyModifiers == command ? e.Key switch
        {
            Key.C => ApplicationCommands.Copy,
            Key.X => ApplicationCommands.Cut,
            Key.V => ApplicationCommands.Paste,
            Key.A => ApplicationCommands.SelectAll,
            Key.Z => ApplicationCommands.Undo,
            Key.Y => ApplicationCommands.Redo,
            _ => null,
        }
        : e.KeyModifiers == (command | KeyModifiers.Shift) && e.Key == Key.Z ? ApplicationCommands.Redo
        : null;
}
