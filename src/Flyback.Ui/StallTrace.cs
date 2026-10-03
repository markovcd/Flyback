using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Threading;

namespace Flyback.Ui;

/// <summary>
/// A file saying every stall over 100 ms: the UI thread held up, a picture's frame, the
/// sound callback, and the step that was running, so a freeze report is one file.
/// </summary>
/// <remarks>
/// Lines are queued and written by a thread of its own, so the sound callback never waits on a disk.
/// Nothing is timed while no trace is open.
/// </remarks>
internal static class StallTrace
{
    /// <summary>How long something runs before it is a stall.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(100);

    private static volatile Sink? sink;
    private static volatile Ended? lastOnUi;

    public static bool On => sink is not null;

    /// <summary>Starts a trace in <paramref name="path"/>, replacing a file already there.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written.</exception>
    public static void Open(string path)
    {
        Close();

        var opened = new Sink(path);

        opened.Add($"# stalls over {Threshold.TotalMilliseconds:0} ms; seconds since the run began, how long, where");
        sink = opened;
    }

    /// <summary>Writes what is queued and closes the file.</summary>
    public static void Close()
    {
        lastOnUi = null;
        Interlocked.Exchange(ref sink, null)?.Dispose();
    }

    /// <summary>Times the work until the result is disposed.</summary>
    public static StallStep Step(string name) => new(name);

    /// <summary>Records <paramref name="took"/> spent in <paramref name="where"/>.</summary>
    /// <param name="step">Whether this is a named step, which a UI-thread stall that overlaps it is put down to.</param>
    public static void Stalled(string where, TimeSpan took, bool step = false)
    {
        if (sink is not { } open) return;

        if (step && Dispatcher.UIThread.CheckAccess()) lastOnUi = new Ended(where, Stopwatch.GetTimestamp());

        open.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{open.Seconds,9:0.000}s {took.TotalMilliseconds,7:0} ms  {where}"));
    }

    /// <summary>The UI thread did not answer for <paramref name="took"/>, since <paramref name="since"/> on the clock.</summary>
    public static void UiStalled(TimeSpan took, long since)
    {
        var inside = lastOnUi is { } ended && ended.At >= since ? $"in {ended.Name}" : "in no named step";

        Stalled($"UI thread ({inside})", took);
    }

    private sealed record Ended(string Name, long At);

    private sealed class Sink : IDisposable
    {
        private readonly BlockingCollection<string> lines = [];
        private readonly StreamWriter writer;
        private readonly Thread thread;
        private readonly long began = Stopwatch.GetTimestamp();

        public Sink(string path)
        {
            writer = new StreamWriter(path, false, new UTF8Encoding(false));

            thread = new Thread(Drain) { IsBackground = true, Name = "stall trace" };
            thread.Start();
        }

        public double Seconds => Stopwatch.GetElapsedTime(began).TotalSeconds;

        public void Add(string line)
        {
            try
            {
                lines.Add(line);
            }
            catch (InvalidOperationException)
            {
                // Closed while this was being said.
            }
        }

        public void Dispose()
        {
            lines.CompleteAdding();
            thread.Join();
            writer.Dispose();
        }

        private void Drain()
        {
            foreach (var line in lines.GetConsumingEnumerable())
            {
                writer.WriteLine(line);
                writer.Flush();
            }
        }
    }
}
