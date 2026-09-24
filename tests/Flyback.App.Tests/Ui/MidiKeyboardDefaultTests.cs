using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The MIDI section's default keyboard layout: applied when a patch gains its
/// first MIDI In, and to nothing that already exists.
/// </summary>
public sealed class MidiKeyboardDefaultTests : UiTest
{
    private static readonly int[] CMajor = [0, 2, 4, 5, 7, 9, 11];

    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-keyboard-default-" + Guid.NewGuid().ToString("N"),
        "output.json");

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainWindow Open(KeyboardLayout layout, Patch? patch = null)
    {
        new OutputSettings { Keyboard = layout }.Save(settingsPath);

        var window = Owned(new MainWindow(new EditorSetup { OutputSettingsPath = settingsPath }));

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        if (patch is not null) Editor(window).History.Open(patch);

        Settle(window);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static void AddFromPalette(MainWindow window, string name, string typeId)
    {
        var editor = Editor(window);
        var at = editor.TranslatePoint(new Point(24, editor.Bounds.Height - 24), window)!.Value;

        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Settle(window);

        var palette = All<ModulePalette>(window).First();
        var box = All<TextBox>(palette).First();

        box.Text = name;
        Settle(window);

        PressKey(box, Key.Enter);
        Settle(window);

        editor.History.Patch.Nodes.ShouldContain(n => n.TypeId == typeId);
    }

    private static void PressKey(InputElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = target,
        });

    [AvaloniaFact]
    public void The_first_MIDI_In_lays_the_keyboard_out_by_scale_when_that_is_the_default()
    {
        var window = Open(KeyboardLayout.Scale);

        AddFromPalette(window, "MIDI In", NodeCatalog.MidiTypeId);

        Editor(window).History.Patch.KeyboardScale.ShouldBe(CMajor);
    }

    [AvaloniaFact]
    public void The_first_MIDI_In_leaves_a_piano_when_that_is_the_default()
    {
        var window = Open(KeyboardLayout.Piano);

        AddFromPalette(window, "MIDI In", NodeCatalog.MidiTypeId);

        Editor(window).History.Patch.KeyboardScale.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_patch_that_already_has_a_MIDI_In_keeps_its_piano()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Add(NodeCatalog.MidiTypeId, 200, 40);

        var window = Open(KeyboardLayout.Scale, b.Patch);

        AddFromPalette(window, "MIDI In", NodeCatalog.MidiTypeId);

        Editor(window).History.Patch.KeyboardScale.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Opening_a_patch_without_a_MIDI_In_is_not_touched()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add(NodeCatalog.OutputTypeId, 700, 40);

        var window = Open(KeyboardLayout.Scale, b.Patch);

        Editor(window).History.Patch.KeyboardScale.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_scale_the_patch_already_chose_is_kept()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        b.Add(NodeCatalog.OutputTypeId, 700, 40);
        b.Patch.KeyboardScale = [0, 3, 7];

        var window = Open(KeyboardLayout.Scale, b.Patch);

        AddFromPalette(window, "MIDI In", NodeCatalog.MidiTypeId);

        Editor(window).History.Patch.KeyboardScale.ShouldBe([0, 3, 7]);
    }

    [AvaloniaFact]
    public void The_default_is_saved_and_read_back()
    {
        new OutputSettings { Keyboard = KeyboardLayout.Scale }.Save(settingsPath);

        OutputSettings.Load(settingsPath).Keyboard.ShouldBe(KeyboardLayout.Scale);
        new OutputSettings().Keyboard.ShouldBe(KeyboardLayout.Piano);
    }
}
