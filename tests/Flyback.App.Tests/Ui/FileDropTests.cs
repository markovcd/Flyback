using System.IO.Compression;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Opening a patch by dropping it on the window from the file explorer, rather
/// than through a picker the headless platform does not put up.
/// </summary>
/// <remarks>
/// Nothing here goes near the operating system's own drag-and-drop: a
/// <see cref="DataTransfer"/> is built by hand and raised as the routed event
/// <see cref="MainWindow"/> actually listens for, which is what lets a route
/// every other file-opening test has to leave to the picker be exercised here.
/// </remarks>
public sealed class FileDropTests : UiTest
{
    private const string Program = GlobalConstants.ApplicationName;

    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "flyback-drop-" + Guid.NewGuid().ToString("N"));

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>A patch with one module beyond its Output, written to disk as a plain <c>.fbk</c>.</summary>
    private string WritePatch(string name)
    {
        Directory.CreateDirectory(folder);

        var patch = new Patch();
        patch.EnsureOutput();
        patch.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 100, 100));

        var path = Path.Combine(folder, $"{name}.fbk");
        File.WriteAllText(path, PatchIO.ToJson(patch));

        return path;
    }

    private MainWindow Open()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    /// <summary>
    /// A real <see cref="IStorageFile"/> over a file on disk, which is what a drop
    /// actually hands the window — <c>IStorageFile</c> itself may not be
    /// implemented outside Avalonia, so this is the platform's own file-backed
    /// implementation, reached by reflection because the type it lives on is
    /// internal.
    /// </summary>
    private static IStorageFile RealStorageFile(string path)
    {
        var type = typeof(IStorageFile).Assembly.GetType(
            "Avalonia.Platform.Storage.FileIO.BclStorageFile", throwOnError: true)!;

        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];

        return (IStorageFile)ctor.Invoke([new FileInfo(path)]);
    }

    private static DataTransfer Carrying(IStorageItem file)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(file));

        return transfer;
    }

    private static DataTransfer CarryingText(string text)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(text));

        return transfer;
    }

    private static void Drop(MainWindow window, IDataTransfer carrying) =>
        window.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, carrying, window, default, KeyModifiers.None));

    private static DragDropEffects DragOver(MainWindow window, IDataTransfer carrying)
    {
        var e = new DragEventArgs(DragDrop.DragOverEvent, carrying, window, default, KeyModifiers.None);
        window.RaiseEvent(e);

        return e.DragEffects;
    }

    /// <summary>
    /// Pumps the dispatcher until <paramref name="until"/> says so, or gives up.
    /// Opening a file crosses a real disk read, which finishes on the thread
    /// pool rather than inside whichever <c>RunJobs</c> call happens to run
    /// first — so this waits on the clock as well as the queue, rather than
    /// spinning through a fixed number of empty pumps.
    /// </summary>
    private static void Pump(Func<bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);

        while (!until() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void WaitForTitleChange(MainWindow window, string? before)
    {
        Pump(() => window.Title != before);
        Settle(window);
    }

    /// <summary>Pumps until the question a replaced patch asks is up. See <c>UnsavedDialogTests.Asking</c>.</summary>
    private static ModalOverlay Asking(MainWindow window)
    {
        Pump(() => All<ModalOverlay>(window).Any());
        Settle(window);

        return All<ModalOverlay>(window).ShouldHaveSingleItem("a replaced patch should have asked about it");
    }

    [AvaloniaFact]
    public void Dropping_a_patch_file_opens_it_and_names_the_window()
    {
        var window = Open();
        var named = window.Title;
        var path = WritePatch("nebula");

        Drop(window, Carrying(RealStorageFile(path)));
        WaitForTitleChange(window, named);

        window.Title.ShouldBe($"nebula — {Program}");

        var patch = Editor(window).Patch;
        patch.Nodes.Count.ShouldBe(2, "the Output and the one module written into the file");
        patch.Nodes.ShouldContain(n => n.TypeId == "value");
    }

    [AvaloniaFact]
    public void Dropping_plain_text_opens_nothing()
    {
        var window = Open();
        var named = window.Title;

        Drop(window, CarryingText("not a file"));
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        window.Title.ShouldBe(named, "nothing was dropped for the window to open");
    }

    [AvaloniaFact]
    public void Dragging_a_file_over_the_window_offers_to_copy_it()
    {
        var window = Open();
        var path = WritePatch("nebula");

        DragOver(window, Carrying(RealStorageFile(path))).ShouldBe(DragDropEffects.Copy);
    }

    [AvaloniaFact]
    public void Dragging_plain_text_over_the_window_refuses_it()
    {
        var window = Open();

        DragOver(window, CarryingText("not a file")).ShouldBe(DragDropEffects.None);
    }

    /// <summary>
    /// A drop is another route to replacing the patch, so it asks the same
    /// question closing an edited window does — see ADR-0068 and
    /// <c>UnsavedDialogTests</c>.
    /// </summary>
    [AvaloniaFact]
    public void Dropping_a_file_over_unsaved_work_asks_first()
    {
        var window = Open();
        var editor = Editor(window);

        editor.AddNode("value").ShouldNotBeNull();
        editor.IsModified.ShouldBeTrue("the window should have something to lose");

        var named = window.Title;
        var path = WritePatch("nebula");

        Drop(window, Carrying(RealStorageFile(path)));

        var dialog = Asking(window);

        All<Button>(dialog).Single(b => b.Content as string == "Discard changes")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        WaitForTitleChange(window, named);

        window.Title.ShouldBe($"nebula — {Program}", "discarding should have let the drop through");
    }

    [AvaloniaFact]
    public void Cancelling_the_question_leaves_the_dropped_file_unopened()
    {
        var window = Open();
        var editor = Editor(window);

        editor.AddNode("value").ShouldNotBeNull();
        var nodes = editor.Patch.Nodes.Count;

        var path = WritePatch("nebula");

        Drop(window, Carrying(RealStorageFile(path)));

        var dialog = Asking(window);

        All<Button>(dialog).Single(b => b.Content as string == "Cancel")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Settle(window);
        Dispatcher.UIThread.RunJobs();

        editor.Patch.Nodes.Count.ShouldBe(nodes, "cancelling should have left the edited patch alone");
    }

    /// <summary>
    /// A bundle written by a later version is refused, the way a patch file
    /// from one is.
    /// </summary>
    /// <remarks>
    /// A bundle reads without throwing whatever is in it, so its load says
    /// whether the patch is all there, and one that is not is refused before
    /// anything calls it the document. Opened empty it would take the bundle's
    /// name, and the next save would write that emptiness over the real file.
    /// </remarks>
    [AvaloniaFact]
    public void A_bundle_from_a_later_version_is_not_opened_as_an_empty_patch()
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "future.fbkb");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry(PatchBundle.PatchEntry).Open()))
        {
            writer.Write("{\"Version\":99,\"Nodes\":[]}");
        }

        var window = Open();
        var title = window.Title;
        var modules = Editor(window).Patch.Nodes.Count;

        modules.ShouldBeGreaterThan(1, "the window opens on a preset");

        Drop(window, Carrying(RealStorageFile(path)));
        WaitForTitleChange(window, title);

        window.IsBundle.ShouldBeFalse("a bundle this build cannot read has not become the document");
        Editor(window).Patch.Nodes.Count.ShouldBe(modules, "and what was open is still open");
        window.Title.ShouldBe(title);
    }

    /// <summary>
    /// A file dropped while Settings is up is refused — it does not replace the
    /// document behind the dialog.
    /// </summary>
    /// <remarks>
    /// A dialog stops the pointer and the keyboard, and a drop arrives by
    /// neither. With nothing unsaved there is no question to stand in its way,
    /// so the drop itself asks whether a dialog is up: a text file opening under
    /// one would take the keyboard with it, and Escape would not reach the
    /// dialog.
    /// </remarks>
    [AvaloniaFact]
    public void A_file_dropped_under_the_settings_dialog_does_not_open_behind_it()
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "dropped.fbks");
        File.WriteAllText(path, "t |> sine(freq: 220) |> out.left");

        var window = Open();
        var title = window.Title;

        All<Button>(window).Single(b => b.Name == "settings")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Pump(() => All<ModalOverlay>(window).Any());
        Settle(window);

        All<ModalOverlay>(window).ShouldNotBeEmpty("Settings is up");

        Drop(window, Carrying(RealStorageFile(path)));
        WaitForTitleChange(window, title);

        window.Title.ShouldBe(title, "the document behind a dialog is not replaced while the dialog is up");
    }

    /// <summary>
    /// A knob opens where its own file left it. Two files share their knobs' ids
    /// whenever one began as a copy of the other, and where the last document's
    /// were turned to says nothing about this one's.
    /// </summary>
    [AvaloniaFact]
    public void A_knob_opens_where_its_file_left_it()
    {
        var window = Open();

        var patch = new Patch();
        patch.EnsureOutput();

        var knob = patch.AddControl("Glow");

        Directory.CreateDirectory(folder);

        knob.Value = 0.2f;
        var quiet = Path.Combine(folder, "quiet.fbk");
        File.WriteAllText(quiet, PatchIO.ToJson(patch));

        knob.Value = 0.8f;
        var loud = Path.Combine(folder, "loud.fbk");
        File.WriteAllText(loud, PatchIO.ToJson(patch));

        var named = window.Title;
        Drop(window, Carrying(RealStorageFile(quiet)));
        WaitForTitleChange(window, named);

        Editor(window).Patch.Control(knob.Id).ShouldNotBeNull().Value.ShouldBe(0.2f);

        named = window.Title;
        Drop(window, Carrying(RealStorageFile(loud)));
        WaitForTitleChange(window, named);

        Editor(window).Patch.Control(knob.Id).ShouldNotBeNull().Value.ShouldBe(0.8f);
    }
}
