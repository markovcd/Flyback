using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// What the title bar says: which patch is on the canvas, and whether there is
/// anything in it to lose.
/// </summary>
/// <remarks>
/// A patch arrives one of three ways and each brings something to call it. The
/// preset is what is checked, because the other two are behind a file picker the
/// headless platform does not put up, and the name is written down in one place for
/// all three.
/// </remarks>
public sealed class WindowTitleTests : UiTest
{
    private const string Program = GlobalConstants.ApplicationName;

    /// <summary>Where the test that saves a file saves it.</summary>
    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "flyback-title-" + Guid.NewGuid().ToString("N"));

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainWindow Open()
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }
    /// <summary>The toolbar's list of patches to start from.</summary>

    private static ComboBox PresetList(MainWindow window) => All<ComboBox>(window)
        .First(box => box.ItemsSource?.OfType<PatchPreset>().Any(p => p.Name == "Plasma") == true);

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    [AvaloniaFact]
    public void The_title_names_the_patch_the_window_opened_on()
    {
        var window = Open();

        // Whatever the list opens on, which is the patch that was built — the
        // first of them that is a patch, the blank canvas heading the list.
        var opening = (PresetList(window).SelectedItem as PatchPreset)?.Name;

        opening.ShouldBe(Presets.All.First(p => p.Kind is not PresetKind.Blank).Name);
        window.Title.ShouldBe($"{opening} — {Program}");
    }

    [AvaloniaFact]
    public void Picking_a_preset_puts_its_name_in_the_title()
    {
        var window = Open();
        var presets = PresetList(window);

        Pick(presets, "Kaleidoscope");
        Settle(window);

        var picked = (presets.SelectedItem as PatchPreset)?.Name;

        picked.ShouldNotBe(Presets.All[0].Name, "a different patch is on the canvas");
        window.Title.ShouldBe($"{picked} — {Program}");
    }

    /// <summary>
    /// The dot marks an edit, appended after the patch's name. It is the only
    /// part of the title that is about the work rather than about which patch
    /// this is.
    /// </summary>
    [AvaloniaFact]
    public void An_edit_marks_the_title_and_undoing_it_takes_the_mark_off()
    {
        var window = Open();
        var editor = Editor(window);

        var named = window.Title.ShouldNotBeNull();

        named.ShouldNotEndWith("•");

        editor.Edits.AddNode("value").ShouldNotBeNull();
        Settle(window);

        window.Title.ShouldBe(named + " •");

        editor.History.Undo().ShouldBeTrue();
        Settle(window);

        window.Title.ShouldBe(named, "back to what was last saved, and still named");
    }

    /// <summary>
    /// Picking a preset over an edited patch renames the title, rather than
    /// leaving it saying what the window used to hold.
    /// </summary>
    [AvaloniaFact]
    public void The_name_follows_the_patch_rather_than_the_window()
    {
        var window = Open();
        var presets = PresetList(window);

        Pick(presets, "Kaleidoscope");
        Settle(window);

        var first = window.Title;

        Pick(presets, "Grid");
        Settle(window);

        window.Title.ShouldNotBe(first);
        window.Title.ShouldBe($"{(presets.SelectedItem as PatchPreset)?.Name} — {Program}");
    }

    /// <summary>
    /// Typing that is nowhere but the buffer marks the title too.
    /// </summary>
    /// <remarks>
    /// The patch has not moved — nothing typed reaches it until somebody applies
    /// it — so the canvas's history has nothing to report, and the dot asked only
    /// that question used to say a document with an afternoon of writing in it
    /// had nothing to lose. It is the same question the unsaved dialog asks when
    /// that window is closed, and the two must not disagree.
    /// </remarks>
    [AvaloniaFact]
    public void Typing_that_is_nowhere_else_marks_the_title()
    {
        var window = Open();

        // Picked from the text view, which reads it into text and leaves the
        // text the document with nothing in it to lose.
        Coding(window).IsChecked = true;
        Settle(window);

        Pick(PresetList(window), "Kaleidoscope");
        Settle(window);

        var named = window.Title.ShouldNotBeNull();

        named.ShouldNotEndWith("•");

        // Through the document, which is what typing is — assigning the text
        // loads one instead, and a document that was loaded is not typing.
        Writing(window).Document.Insert(0, "# a note to myself\n");
        Settle(window);

        window.Title.ShouldBe(named + " •");
    }

    /// <summary>
    /// A text document saved, then stepped back and forward across the apply
    /// that made it one, is still saved.
    /// </summary>
    /// <remarks>
    /// What is on disk is a fact about the disk rather than about a step, so a
    /// save restates it beside every step the text owns. The redo then hands
    /// back the document as the file has it, not as it stood when the apply was
    /// recorded, with nothing written anywhere.
    /// </remarks>
    [AvaloniaFact]
    public async Task Undo_and_redo_across_the_handover_do_not_unsave_a_saved_text()
    {
        var window = Open();

        Coding(window).IsChecked = true;
        Settle(window);

        Press(window, "apply");

        Directory.CreateDirectory(folder);

        (await window.SaveToAsync(RealStorageFile(Path.Combine(folder, "saved.fbks")))).ShouldBeTrue();
        Settle(window);

        window.Title.ShouldNotBeNull().ShouldNotEndWith("•");

        Press(window, "undo");
        Press(window, "redo");

        Editor(window).History.Locked.ShouldBeTrue("the text is the document again");
        window.Title.ShouldNotBeNull().ShouldNotEndWith("•", customMessage: "and it is the text that was saved");
    }

    private static ToggleButton Coding(MainWindow window) =>
        All<ToggleButton>(window).Single(b => b.Name == "code");

    private static AvaloniaEdit.TextEditor Writing(MainWindow window) =>
        All<AvaloniaEdit.TextEditor>(window).Single(b => b.Name == "source");

    /// <summary>Presses the toolbar button of this name, the way the mouse would.</summary>
    private static void Press(MainWindow window, string named)
    {
        All<Button>(window).Single(b => b.Name == named).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    /// <summary>The platform's own file-backed storage file — see <c>FileDropTests</c>.</summary>
    private static IStorageFile RealStorageFile(string path)
    {
        var type = typeof(IStorageFile).Assembly.GetType(
            "Avalonia.Platform.Storage.FileIO.BclStorageFile", throwOnError: true)!;

        var flags = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance;

        return (IStorageFile)type.GetConstructors(flags)[0].Invoke([new FileInfo(path)]);
    }
}
