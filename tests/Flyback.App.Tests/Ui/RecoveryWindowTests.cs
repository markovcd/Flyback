using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Work a crash left behind, put back on the canvas.
/// </summary>
/// <remarks>
/// The question at startup is behind a dialog; what is pinned here is what
/// answering Restore does, which is the part that could lose the work a second time.
/// </remarks>
public class RecoveryWindowTests : UiTest
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "flyback-recovery-window-" + Guid.NewGuid().ToString("N"));

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private MainWindow Open(string? recoveryFolder = null)
    {
        var window = NewMainWindow(new EditorSetup { RecoveryFolder = recoveryFolder });

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static RecoveredWork Work(Patch patch, string? source = null) =>
        new("drift", Beside: null, PatchIO.ToJson(patch), source, Conversation: null, Files: null);

    private static Patch APatch() => Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

    [AvaloniaFact]
    public void Restored_work_is_the_document_and_is_unsaved()
    {
        var window = Open();
        var patch = APatch();

        window.Recover(Work(patch)).ShouldBeTrue();

        All<NodeEditor>(window).Single().History.Patch.Nodes.Count.ShouldBe(patch.Nodes.Count);
        window.Title.ShouldBe($"drift — {GlobalConstants.ApplicationName} •");
    }

    [AvaloniaFact]
    public void Restored_text_is_the_document_and_is_unsaved()
    {
        var window = Open();
        var text = PatchPrinter.Print(APatch());

        window.Recover(Work(APatch(), source: text)).ShouldBeTrue();

        All<SourceView>(window).Single().Source.ShouldBe(text);
        window.Title.ShouldEndWith("•");
    }

    [AvaloniaFact]
    public void A_start_after_a_crash_restores_without_asking_and_says_so()
    {
        var crashed = Recovery.Open(folder)!;

        crashed.Keep(Work(APatch()));
        crashed.Abandon();

        var window = Open(folder);

        Dispatcher.UIThread.RunJobs();

        window.Title.ShouldBe($"drift — {GlobalConstants.ApplicationName} •");
        All<ReportLine>(window).Single().History.ShouldContain(line => line.StartsWith("Restored drift"));
        Recovery.Orphans(folder).ShouldBeEmpty("the snapshot was restored, so nothing is left to offer");

        // Saved, so the close asks nothing and lets go of the lock.
        All<NodeEditor>(window).Single().History.MarkSaved();
        window.Close();
    }

    [AvaloniaFact]
    public void Restored_work_is_kept_at_once_and_let_go_on_closing()
    {
        var window = Open(folder);

        window.Recover(Work(APatch()));

        Directory.EnumerateFiles(folder, "*.json").ShouldHaveSingleItem();
        Recovery.Orphans(folder).ShouldBeEmpty("the window that restored it is still running");

        // Saved, so the close asks nothing and goes through.
        All<NodeEditor>(window).Single().History.MarkSaved();
        window.Close();

        Directory.EnumerateFiles(folder).ShouldBeEmpty("a window closed on purpose leaves nothing to offer");
    }
}
