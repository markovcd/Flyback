using Avalonia.Threading;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Engine.Graph;
using Flyback.Engine.Mix;
using Flyback.Engine.Render;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Canvas;

/// <summary>
/// Listens to the patch offline from the playhead and shows what a mix check found
/// in a panel over the window.
/// </summary>
/// <remarks>
/// Only on demand and only where <see cref="EditorHost.Measures"/>: a few seconds of
/// sound rendered twice over is too much for a phone to do unasked. On a copy of the
/// patch and off the UI thread; asking again while it runs stops it.
/// </remarks>
internal sealed class MixChecking(
    NodeEditor editor,
    PreviewHost preview,
    ReportLine report,
    IDialog dialog,
    EditorHost host) : IReactTo<MixCheckAsked>, IReactTo<DocumentArrived>, IReactTo<FilesMoved>, IDisposable
{
    /// <summary>Where the patch's sound files and pictures are read from, as the document last said.</summary>
    private (ISampleLibrary Sounds, IImageLibrary Pictures) files = (new SampleLibrary(), new ImageLibrary());

    private CancellationTokenSource? running;

    /// <summary>Whether a check is under way; tests read it back.</summary>
    public bool Running => running is not null;

    public Task On(DocumentArrived notice)
    {
        files = (notice.Sounds, notice.Pictures);
        return Task.CompletedTask;
    }

    public Task On(FilesMoved notice)
    {
        files = (notice.Sounds, notice.Pictures);
        return Task.CompletedTask;
    }

    public async Task On(MixCheckAsked notice)
    {
        if (await CheckAsync() is { } found) await dialog.Show("Mix check", MixPanel.View(found));
    }

    /// <summary>Stops a check still running when the window goes.</summary>
    public void Dispose() => running?.Cancel();

    /// <summary>Checks the mix and hands back what it found; null where it was stopped, refused or stops one running.</summary>
    public async Task<MixReport?> CheckAsync(double seconds = MixOptions.DefaultSeconds)
    {
        if (!host.Measures) return null;

        if (running is { } busy)
        {
            await busy.CancelAsync();
            return null;
        }

        var catalog = NodeCatalog.Current;
        var patch = PatchIO.Read(PatchIO.ToJson(editor.History.Patch, catalog), catalog).Patch;
        var options = new MixOptions(seconds, Math.Max(0d, preview.Time));
        var (sounds, pictures) = files;

        var shown = -1;
        var progress = new Progress<double>(done =>
        {
            var percent = (int)(done * 100);
            if (percent == shown || running is null) return;

            shown = percent;
            report.Say($"Checking the mix… {percent}%", progress: true);
        });

        using var stop = running = new CancellationTokenSource();

        try
        {
            var found = await Task.Run(
                () => MixCheck.Take(patch, options, catalog, sounds, pictures, progress: progress, cancel: stop.Token),
                stop.Token);

            Dispatcher.UIThread.VerifyAccess();

            var warnings = found.Findings.Count(f => f.Severity == MixSeverity.Warning);

            report.Say(found.Findings.Count == 0
                ? "The mix check found nothing to flag."
                : $"The mix check found {found.Findings.Count} thing{(found.Findings.Count == 1 ? string.Empty : "s")}, "
                  + $"{warnings} of them warning{(warnings == 1 ? string.Empty : "s")}.");

            return found;
        }
        catch (OperationCanceledException)
        {
            report.Say("Mix check stopped.");
            return null;
        }
        finally
        {
            running = null;
        }
    }
}
