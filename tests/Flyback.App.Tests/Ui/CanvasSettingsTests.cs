using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Canvas tab of the settings window: the two switches over what a plugin is
/// allowed to draw of its own module (ADR-0118).
/// </summary>
/// <remarks>
/// Both take effect on the canvas as they are saved rather than at the next
/// start, which is the whole point of the second one — somebody clearing it is
/// asking for the thing in front of them to stop moving.
/// </remarks>
public sealed class CanvasSettingsTests : UiTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-canvas-settings-" + Guid.NewGuid().ToString("N"),
        "canvas.json");

    private const int CanvasTab = 1;

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        // A setting of the running program, not of the window — see ModuleSkins.
        ModuleSkins.Honored = true;
        ModuleSkins.Animated = true;
        NodeGeometry.Compact = false;
    }

    private MainWindow Open(string? settingsPath = null)
    {
        var window = Owned(new MainWindow(new EditorSetup { CanvasSettingsPath = settingsPath }));

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ModalOverlay OpenSettings(MainWindow window)
    {
        All<Button>(window).Single(b => b.Name == "settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        var dialog = All<ModalOverlay>(window).Single();

        All<TabControl>(dialog).Single(t => t.Name == "settingsTabs").SelectedIndex = CanvasTab;
        Settle(window);

        return dialog;
    }

    private static void Close(MainWindow window, ModalOverlay dialog, string by)
    {
        All<Button>(dialog)
            .Single(b => b.Content as string == by)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    private static CheckBox Skins(ModalOverlay dialog) =>
        All<CheckBox>(dialog).Single(c => c.Name == "pluginSkins");

    private static CheckBox Animation(ModalOverlay dialog) =>
        All<CheckBox>(dialog).Single(c => c.Name == "animateSkins");

    private static CheckBox Compact(ModalOverlay dialog) =>
        All<CheckBox>(dialog).Single(c => c.Name == "compactModules");

    [AvaloniaFact]
    public void A_plugin_paints_its_own_modules_until_switched_off()
    {
        var dialog = OpenSettings(Open(settingsPath));

        Skins(dialog).IsChecked.ShouldBe(true);
        Animation(dialog).IsChecked.ShouldBe(true);
        Compact(dialog).IsChecked.ShouldBe(false);
    }

    [AvaloniaFact]
    public void Switching_compact_modules_on_puts_inputs_beside_outputs()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);
        var filter = NodeCatalog.Require(NodeCatalog.FilterTypeId);
        var node = NodeInstance.Create(filter, 0, 0);

        Compact(dialog).IsChecked = true;
        Close(window, dialog, "Save");

        NodeGeometry.Compact.ShouldBeTrue();
        NodeGeometry.InputPort(node, filter, 0).Y.ShouldBe(NodeGeometry.OutputPort(node, 0).Y);
        CanvasSettings.Load(settingsPath).CompactModules.ShouldBeTrue();

        Compact(OpenSettings(Open(settingsPath))).IsChecked.ShouldBe(true);
    }

    /// <summary>
    /// Switched off, a skinned module is its category again — which is what the
    /// canvas asks <see cref="ModuleSkins"/> for, so this is the whole of the
    /// effect rather than a flag beside it.
    /// </summary>
    [AvaloniaFact]
    public void Switching_the_skins_off_draws_every_module_as_its_category()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        var def = NodeCatalog.Require("math.add") with
        {
            Skin = new ModuleSkin.Palette(new Swatch(0x2E, 0x8B, 0x57)),
        };

        ModuleSkins.Of(def).ShouldNotBeNull();

        Skins(dialog).IsChecked = false;
        Close(window, dialog, "Save");

        ModuleSkins.Of(def).ShouldBeNull();
        Colors.Palette(def).Accent.ShouldBe(Colors.Accent(ModuleCategories.Maths));
    }

    [AvaloniaFact]
    public void Switching_the_animation_off_holds_every_picture_still()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        Animation(dialog).IsChecked = false;
        Close(window, dialog, "Save");

        ModuleSkins.Animated.ShouldBeFalse();
        ModuleSkins.Honored.ShouldBeTrue("the two switches are separate complaints");
    }

    [AvaloniaFact]
    public void What_is_saved_is_read_back_at_the_next_launch()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        Skins(dialog).IsChecked = false;
        Animation(dialog).IsChecked = false;
        Close(window, dialog, "Save");

        var saved = CanvasSettings.Load(settingsPath);

        saved.PluginSkins.ShouldBeFalse();
        saved.AnimateSkins.ShouldBeFalse();

        var next = OpenSettings(Open(settingsPath));

        Skins(next).IsChecked.ShouldBe(false);
        Animation(next).IsChecked.ShouldBe(false);
    }

    private static AvaloniaEdit.TextEditor ShowText(MainWindow window)
    {
        All<ToggleButton>(window).Single(b => b.Name == "code").IsChecked = true;
        Settle(window);

        return All<AvaloniaEdit.TextEditor>(window).Single(t => t.Name == "source");
    }

    private static void Wheel(MainWindow window, Control over, double notches, RawInputModifiers held)
    {
        var at = over.TranslatePoint(new Point(over.Bounds.Width / 2, over.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the control is not in this window");

        window.MouseWheel(at, new Vector(0, notches), held);
        Settle(window);
    }

    [AvaloniaFact]
    public void Ctrl_scrolling_over_the_text_resizes_it_and_the_size_is_read_back_at_the_next_launch()
    {
        var window = Open(settingsPath);
        var text = ShowText(window);
        var before = text.FontSize;

        Wheel(window, text, 1, RawInputModifiers.Control);
        Wheel(window, text, 1, RawInputModifiers.Control);
        Wheel(window, text, -1, RawInputModifiers.Control);

        text.FontSize.ShouldBe(before + 1);
        CanvasSettings.Load(settingsPath).EditorFontSize.ShouldBe(before + 1);

        ShowText(Open(settingsPath)).FontSize.ShouldBe(before + 1);
    }

    [AvaloniaFact]
    public void Ctrl_plus_and_minus_resize_the_text_and_Ctrl_0_puts_it_back()
    {
        var window = Open(settingsPath);
        var text = ShowText(window);

        text.TextArea.Focus();
        Settle(window);

        var shown = text.Text;

        window.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, "=");
        window.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, "=");
        window.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, "-");
        Settle(window);

        text.FontSize.ShouldBe(CanvasSettings.DefaultEditorFontSize + 1);
        CanvasSettings.Load(settingsPath).EditorFontSize.ShouldBe(CanvasSettings.DefaultEditorFontSize + 1);

        window.KeyPress(Key.D0, RawInputModifiers.Control, PhysicalKey.Digit0, "0");
        Settle(window);

        text.FontSize.ShouldBe(CanvasSettings.DefaultEditorFontSize);
        text.Text.ShouldBe(shown);
        CanvasSettings.Load(settingsPath).EditorFontSize.ShouldBe(CanvasSettings.DefaultEditorFontSize);
    }

    [AvaloniaFact]
    public void Scrolling_over_the_text_without_Ctrl_leaves_its_size_alone()
    {
        var window = Open(settingsPath);
        var text = ShowText(window);
        var before = text.FontSize;

        Wheel(window, text, 1, RawInputModifiers.None);

        text.FontSize.ShouldBe(before);
        File.Exists(settingsPath).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_text_size_stops_at_its_limits()
    {
        var window = Open(settingsPath);
        var text = ShowText(window);

        for (var i = 0; i < 60; i++) Wheel(window, text, 1, RawInputModifiers.Control);

        text.FontSize.ShouldBe(CanvasSettings.MaxEditorFontSize);

        for (var i = 0; i < 60; i++) Wheel(window, text, -1, RawInputModifiers.Control);

        text.FontSize.ShouldBe(CanvasSettings.MinEditorFontSize);
    }

    [AvaloniaFact]
    public void Saving_the_switches_keeps_the_text_size()
    {
        var window = Open(settingsPath);

        Wheel(window, ShowText(window), 1, RawInputModifiers.Control);

        var dialog = OpenSettings(window);

        Skins(dialog).IsChecked = false;
        Close(window, dialog, "Save");

        CanvasSettings.Load(settingsPath).EditorFontSize.ShouldBe(CanvasSettings.DefaultEditorFontSize + 1);
    }

    [AvaloniaFact]
    public void Cancel_puts_the_switches_back()
    {
        var window = Open(settingsPath);
        var dialog = OpenSettings(window);

        Skins(dialog).IsChecked = false;
        Animation(dialog).IsChecked = false;
        Close(window, dialog, "Cancel");

        File.Exists(settingsPath).ShouldBeFalse();

        ModuleSkins.Honored.ShouldBeTrue("nothing was saved, so nothing changed on the canvas");
        ModuleSkins.Animated.ShouldBeTrue();

        var again = OpenSettings(window);

        Skins(again).IsChecked.ShouldBe(true);
        Animation(again).IsChecked.ShouldBe(true);
    }
}
