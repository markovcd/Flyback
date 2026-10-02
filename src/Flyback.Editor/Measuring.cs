using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Engine.Graph;
using Flyback.Engine.Measure;
using Flyback.Ui.Controls;

namespace Flyback.Editor;

/// <summary>
/// Runs the patch offline from the playhead and pins what each output carried beside
/// it: the selected modules' outputs, or every module's when none is selected.
/// </summary>
/// <remarks>
/// On a copy of the patch and off the UI thread, so editing carries on while it runs;
/// an edit made meanwhile leaves the result out of date the moment it lands. Asking
/// again while it runs stops it, and while its labels are fresh takes them down. Not
/// in a page, where a pass of seconds on the one thread a page has would hang it.
/// </remarks>
internal sealed class Measuring(
    NodeEditor editor,
    Playback playback,
    PreviewHost preview,
    CanvasSection settings,
    MeasureLabels labels,
    ReportLine report,
    Reactions reactions,
    EditorHost host) : IReactTo<MeasureAsked>, IDisposable
{
    private CancellationTokenSource? running;

    /// <summary>Whether a measurement is under way.</summary>
    public bool Running => running is not null;

    /// <summary>Measures, or takes fresh labels down, or stops a run under way: one gesture for all three.</summary>
    public async Task On(MeasureAsked notice)
    {
        if (running is null && labels.Report is not null && !labels.Stale)
        {
            Hide();
            return;
        }

        await MeasureAsync();
    }

    /// <summary>Takes every measurement down, off the canvas and the inspector.</summary>
    public void Hide()
    {
        labels.Clear();
        reactions.Raise(new PanelStale());
        report.Say("Measurements hidden. Measure again to pin them.");
    }

    /// <summary>Stops a measurement still running when the window goes.</summary>
    public void Dispose() => running?.Cancel();

    /// <summary>Measures, pins the result and hands it back; null where it was stopped, refused or stops one running.</summary>
    /// <param name="window">How long to run, or null for the window Settings → Canvas gives.</param>
    public async Task<MeasureReport?> MeasureAsync(double? window = null)
    {
        var seconds = window ?? settings.MeasureSeconds;

        if (host.InPage) return null;

        if (running is { } busy)
        {
            await busy.CancelAsync();
            return null;
        }

        var catalog = NodeCatalog.Current;
        var patch = PatchIO.Read(PatchIO.ToJson(editor.History.Patch, catalog), catalog).Patch;
        var chosen = editor.Selection.Ids.Count > 0 ? editor.Selection.Ids.ToArray() : null;
        var options = new MeasureOptions(seconds, Math.Max(0d, preview.Time), chosen);
        var (sounds, pictures) = playback.Files;

        var shown = -1;
        var progress = new Progress<double>(done =>
        {
            var percent = (int)(done * 100);
            if (percent == shown || running is null) return;

            shown = percent;
            report.Say($"Measuring… {percent}%", progress: true);
        });

        using var stop = running = new CancellationTokenSource();

        try
        {
            var measured = await Task.Run(
                () => Measurements.Take(patch, options, catalog, sounds, pictures, progress, stop.Token),
                stop.Token);

            Dispatcher.UIThread.VerifyAccess();

            labels.Show(measured);
            reactions.Raise(new PanelStale());

            report.Say(
                $"Measured {measured.Measurements.Count} output{(measured.Measurements.Count == 1 ? string.Empty : "s")} "
                + $"over {MeasurementWords.Number(seconds)} s from {MeasurementWords.Number(options.From)} s, with nothing played in.");

            return measured;
        }
        catch (OperationCanceledException)
        {
            report.Say("Measuring stopped.");
            return null;
        }
        finally
        {
            running = null;
        }
    }
}
