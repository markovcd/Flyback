using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Core;
using Flyback.Editor.Hubs;
using Flyback.Editor.Windows;
using Flyback.Host;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Shell;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Settings;

/// <summary>
/// The Live patching section: how solid the modules and the band behind the text are over
/// the picture, when each fades, and whether the pointer brings faded text back.
/// </summary>
public class LivePatchingSettingsTests : OutputSettingsTest
{
    private const string Tab = PictureTab;

    private static NumericUpDown Number(Visual within, string name) => All<NumericUpDown>(within).Single(c => c.Name == name);

    private static void Set(MainWindow window, Action<ModalOverlay> change)
    {
        var dialog = OpenSettings(window, Tab);
        change(dialog);
        Dispatcher.UIThread.RunJobs();
        CloseSettings(window, dialog);
    }

    [AvaloniaFact]
    public void The_opacities_reach_the_patch_on_save_and_are_kept()
    {
        var window = Open(settingsPath);

        Set(window, dialog =>
        {
            Number(dialog, "moduleOpacity").Value = 50;
            Number(dialog, "textOpacity").Value = 30;
        });

        Editor(window).GlassOpacity.ShouldBe(0.5, 1e-9);
        All<SourceView>(window).Single().BandOpacity.ShouldBe(0.3, 1e-9);

        var kept = OutputSettings.Load(settingsPath).Live;
        kept.ModuleOpacity.ShouldBe(50);
        kept.TextOpacity.ShouldBe(30);
    }

    [AvaloniaFact]
    public void Text_over_the_picture_fades_when_left_alone_and_comes_back_at_a_key()
    {
        var window = Open(settingsPath);
        var live = Service<LivePatching>(window);

        window.KeyPress(Avalonia.Input.Key.F2, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F2, null);
        live.Toggle();
        Settle(window);
        live.Coding.ShouldBeTrue();

        All<AvaloniaEdit.Editing.TextArea>(window).Single().Focus();
        live.Fade.Fade();
        live.Fade.IsFaded.ShouldBeTrue();

        window.KeyPress(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowRight, null);
        Settle(window);

        live.Fade.IsFaded.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Keys_only_leaves_faded_text_hidden_while_the_pointer_moves()
    {
        var window = Open(settingsPath);
        var live = Service<LivePatching>(window);

        Set(window, dialog => All<ComboBox>(dialog).Single(c => c.Name == "textWakes").SelectedIndex = 1);

        window.KeyPress(Avalonia.Input.Key.F2, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F2, null);
        live.Toggle();
        Settle(window);

        live.Fade.Fade();
        window.MouseMove(new Point(window.Bounds.Width / 2, window.Bounds.Height / 2));
        Settle(window);

        live.Fade.IsFaded.ShouldBeTrue("the pointer does not bring it back");
        OutputSettings.Load(settingsPath).Live.TextWakesOnPointer.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Keys_only_faded_text_takes_no_pointer()
    {
        var window = Open(settingsPath);
        var live = Service<LivePatching>(window);

        Set(window, dialog => All<ComboBox>(dialog).Single(c => c.Name == "textWakes").SelectedIndex = 1);

        window.KeyPress(Avalonia.Input.Key.F2, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F2, null);
        live.Toggle();
        Settle(window);

        var area = All<AvaloniaEdit.Editing.TextArea>(window).Single();
        area.Document.TextLength.ShouldBeGreaterThan(0);
        var from = area.TranslatePoint(new Point(4, 6), window)!.Value;

        live.Fade.Fade();
        window.MouseDown(from, Avalonia.Input.MouseButton.Left);
        window.MouseMove(from + new Point(200, 40));
        window.MouseUp(from + new Point(200, 40), Avalonia.Input.MouseButton.Left);
        Settle(window);

        area.Selection.IsEmpty.ShouldBeTrue("faded text can't be selected with the pointer");
        live.Fade.IsFaded.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_wait_of_nought_never_fades_the_modules()
    {
        var window = Open(settingsPath);
        var live = Service<LivePatching>(window);

        Set(window, dialog => Number(dialog, "moduleFade").Value = 0);

        live.Toggle();
        Settle(window);
        live.Fade.Fade();

        live.Fade.IsFaded.ShouldBeFalse();
        Editor(window).Opacity.ShouldBe(1);
    }

    [AvaloniaTheory]
    [InlineData("""{ "live": { "moduleOpacity": 0, "textOpacity": 300, "moduleFadeSeconds": -4, "textFadeSeconds": 1e9 } }""", 10, 100, 0, 600)]
    [InlineData("""{ "live": null }""", 78, 72, 3, 5)]
    [InlineData("""{ }""", 78, 72, 3, 5)]
    public void A_hand_edited_look_reads_in_range(string section, int module, int text, double moduleFade, double textFade)
    {
        SettingsFile.Write(settingsPath, OutputSettings.Section, section);

        var look = OutputSettings.Load(settingsPath).Live;

        look.ModuleOpacity.ShouldBe(module);
        look.TextOpacity.ShouldBe(text);
        look.ModuleFadeSeconds.ShouldBe(moduleFade);
        look.TextFadeSeconds.ShouldBe(textFade);
    }
}
