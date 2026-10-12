using System.Collections.Concurrent;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// The single-threaded apartment an ASIO driver lives in, from its creation to its release.
/// </summary>
/// <remarks>
/// A driver has no proxy, so it cannot be reached from any apartment but the one that made it,
/// and many drivers post to that thread's window messages; a wait on an STA thread pumps them.
/// The driver's own audio thread calls back directly and never passes through here.
/// </remarks>
internal sealed class AsioThread : IDisposable
{
    /// <summary>How long a driver call is waited on: ASIO4ALL takes a second or two to open its card.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;
    private readonly TimeSpan patience;

    /// <summary>Set once a call has outlasted the patience, which leaves the thread stuck inside the driver.</summary>
    private volatile bool wedged;

    /// <param name="patience">How long a call may take before the driver is given up on, so a driver that hangs never hangs its caller.</param>
    public AsioThread(TimeSpan? patience = null)
    {
        this.patience = patience ?? Patience;
        thread = new Thread(Run) { IsBackground = true, Name = "ASIO driver" };

        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
    }

    /// <summary>
    /// Runs <paramref name="job"/> on the driver's thread and waits for it, throwing what it threw.
    /// </summary>
    /// <exception cref="TimeoutException">The driver did not answer in time, now or on an earlier call.</exception>
    public void Invoke(Action job)
    {
        if (Thread.CurrentThread == thread)
        {
            job();
            return;
        }

        if (wedged) throw Unanswered();

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        work.Add(() =>
        {
            try
            {
                job();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });

        if (!((IAsyncResult)done.Task).AsyncWaitHandle.WaitOne(patience))
        {
            wedged = true;
            throw Unanswered();
        }

        done.Task.GetAwaiter().GetResult();
    }

    /// <summary>Queues <paramref name="job"/> behind whatever the driver's thread is stuck in, without waiting.</summary>
    public void Post(Action job) => work.Add(job);

    /// <summary>Lets the thread finish its queue. Waits for it, unless it is stuck inside the driver.</summary>
    public void Dispose()
    {
        work.CompleteAdding();

        if (Thread.CurrentThread == thread || wedged) return;

        thread.Join();
        work.Dispose();
    }

    private TimeoutException Unanswered() =>
        new($"the driver did not answer in {patience.TotalSeconds:0.#} seconds; restart Flyback to try it again.");

    private void Run()
    {
        foreach (var job in work.GetConsumingEnumerable()) job();
    }
}
