using Flyback.Core.Graph;
using Flyback.Plugins.Decide;

namespace Flyback.Editor.Decide;

/// <summary>
/// Says a compile's complaints again likeliest first, once the decision model has judged
/// them, unless the patch was compiled again in the meantime.
/// </summary>
internal sealed class IssueOrdering(Decisions decisions, ReportLine report)
{
    private CancellationTokenSource? asking;

    /// <summary>How long the patch has to stay as it is before its complaints are judged: an edit recompiles it on every step.</summary>
    internal static TimeSpan Pause { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The last reordering, for the tests to wait on.</summary>
    internal Task Pending { get; private set; } = Task.CompletedTask;

    /// <param name="lead">What the line says ahead of the complaints, which keeps its place.</param>
    /// <param name="issues">The complaints, in the compiler's order.</param>
    public void Order(IReadOnlyList<string> lead, IReadOnlyList<string> issues, Patch patch)
    {
        asking?.Cancel();
        asking = null;

        if (issues.Count < 2 || decisions.Chosen is null) return;

        var stop = new CancellationTokenSource();
        asking = stop;

        Pending = Asked();

        async Task Asked()
        {
            try
            {
                await Task.Delay(Pause, stop.Token);

                var likely = await new IssueTriage(decisions).Likelihoods(issues, stop.Token);

                if (stop.IsCancellationRequested || likely is null) return;

                report.Say([.. lead, .. IssueTriage.Ordered(issues, likely)]);
            }
            catch (OperationCanceledException)
            {
                // Compiled again; that compile asks for itself.
            }
        }
    }
}
