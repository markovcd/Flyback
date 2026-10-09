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
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;

    public AsioThread()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "ASIO driver" };

        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
    }

    /// <summary>Runs <paramref name="job"/> on the driver's thread and waits for it, throwing what it threw.</summary>
    public void Invoke(Action job)
    {
        if (Thread.CurrentThread == thread)
        {
            job();
            return;
        }

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

        done.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        work.CompleteAdding();

        if (Thread.CurrentThread == thread) return;

        thread.Join();
        work.Dispose();
    }

    private void Run()
    {
        foreach (var job in work.GetConsumingEnumerable()) job();
    }
}
