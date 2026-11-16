using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AvaloniaEdit;
using Flyback.Editor.Hubs;
using Flyback.Ui.Testing;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

/// <summary>
/// The text takes the platform's command key for the clipboard and undo where it is not
/// the one the editor's own commands listen for, as Cmd in a browser on a Mac.
/// </summary>
public class SourceCommandKeysTests : UiTest
{
    /// <summary>The command key the editor does not listen for on this machine: Cmd off a Mac, Ctrl on one.</summary>
    private static readonly KeyModifiers Other =
        ApplicationCommands.Copy.Gesture.KeyModifiers == KeyModifiers.Meta ? KeyModifiers.Control : KeyModifiers.Meta;

    private (Window Window, TextEditor Text) Open(string text)
    {
        var editor = new TextEditor { Text = text };
        SourceCommandKeys.Attach(editor, () => Other);

        var window = Owned(new Window { Content = editor, Width = 400, Height = 300 });
        window.Show();
        editor.TextArea.Focus();
        Settle(window);

        return (window, editor);
    }

    private static void Command(Window window, PhysicalKey key, RawInputModifiers also = RawInputModifiers.None)
    {
        var held = Other == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        window.KeyPressQwerty(key, held | also);
        Settle(window);
    }

    private static string? Clipboard(Window window) =>
        TopLevel.GetTopLevel(window)!.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult();

    [AvaloniaFact]
    public void Command_A_and_command_C_copy_the_whole_text()
    {
        var (window, _) = Open("let hum = t |> sine(freq: 220)");

        Command(window, PhysicalKey.A);
        Command(window, PhysicalKey.C);

        Pump(() => Clipboard(window) is { Length: > 0 }, window);
        Clipboard(window).ShouldBe("let hum = t |> sine(freq: 220)");
    }

    [AvaloniaFact]
    public void Command_X_cuts_and_command_V_puts_it_back()
    {
        var (window, text) = Open("hum |> out.left");
        text.Select(0, 4);

        Command(window, PhysicalKey.X);

        text.Text.ShouldBe("|> out.left");
        Pump(() => Clipboard(window) == "hum ", window);

        text.CaretOffset = text.Text.Length;
        Command(window, PhysicalKey.V);

        Pump(() => text.Text == "|> out.lefthum ", window);
    }

    [AvaloniaFact]
    public void Command_Z_undoes_and_command_Shift_Z_redoes()
    {
        var (window, text) = Open("hum");
        text.Document.Insert(3, "!");

        Command(window, PhysicalKey.Z);
        text.Text.ShouldBe("hum");

        Command(window, PhysicalKey.Z, RawInputModifiers.Shift);
        text.Text.ShouldBe("hum!");
    }
}
