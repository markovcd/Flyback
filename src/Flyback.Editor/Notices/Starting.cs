using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace Flyback.Editor.Notices;

/// <summary>Runs the parts that start at each <see cref="StartPhase"/>, each phase once.</summary>
internal sealed class Starting(IEnumerable<IStartAt> parts)
{
    private readonly HashSet<StartPhase> run = [];

    /// <summary>Runs every part of <paramref name="phase"/> in turn, waiting for each before the next.</summary>
    public async Task RunAsync(StartPhase phase)
    {
        if (!run.Add(phase)) return;

        foreach (var part in parts.Where(part => part.Phase == phase)) await part.On();
    }

    /// <summary>
    /// The same, for a host that cannot wait. A fault after the first await is thrown on
    /// the UI thread, as <see cref="Reactions.Raise{T}"/> throws one.
    /// </summary>
    public void Run(StartPhase phase)
    {
        var running = RunAsync(phase);

        if (running.IsCompleted)
        {
            running.GetAwaiter().GetResult();
            return;
        }

        running.ContinueWith(
            faulted => Dispatcher.UIThread.Post(() => ExceptionDispatchInfo.Capture(faulted.Exception!.GetBaseException()).Throw()),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
