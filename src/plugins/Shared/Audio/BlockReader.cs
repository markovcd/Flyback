using Flyback.Core;

namespace Flyback.Plugins.Audio;

/// <summary>
/// The capture thread of an input whose reads block until a block has arrived: opens the
/// input, reads and delivers until stopped or refused, then closes it itself.
/// </summary>
/// <remarks>
/// As with <see cref="BlockWriter"/>, a thread that outlives <see cref="Stop"/> can never
/// reach a later reader's callback or input.
/// </remarks>
internal sealed class BlockReader
{
    /// <summary>Long enough that a slow input is not taken for one that has stopped answering.</summary>
    private const int ExitMilliseconds = 2000;

    private readonly Thread thread;
    private readonly AudioCaptureCallback deliver;
    private readonly Func<Func<bool>, BlockSource?> open;
    private volatile bool running = true;

    private BlockReader(AudioCaptureCallback deliver, Func<Func<bool>, BlockSource?> open)
    {
        (this.deliver, this.open) = (deliver, open);
        thread = new Thread(Run) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} input" };
    }

    /// <summary>Whether the thread is still opening or reading.</summary>
    public bool IsRunning => running;

    /// <summary>
    /// Starts the thread. <paramref name="open"/> runs on it first, given whether reading is
    /// still wanted, and returns null where there is nothing to read from.
    /// </summary>
    public static BlockReader Start(AudioCaptureCallback deliver, Func<Func<bool>, BlockSource?> open)
    {
        var reader = new BlockReader(deliver, open);
        reader.thread.Start();
        return reader;
    }

    /// <summary>
    /// Asks the thread to end and waits for it to close the input. An input that has
    /// stopped answering is left to its thread, which closes it if the read ever returns.
    /// </summary>
    public void Stop(int waitMilliseconds = ExitMilliseconds)
    {
        running = false;
        thread.Join(waitMilliseconds);
    }

    private void Run()
    {
        BlockSource? source = null;

        try
        {
            source = open(() => running);

            if (source is { } input)
            {
                while (running)
                {
                    var samples = input.Read(input.Block);
                    if (samples < 0) break;

                    if (samples > 0) deliver(input.Block.AsSpan(0, samples));
                }
            }
        }
        catch
        {
            // An exception leaving this thread would end the process rather than the listening.
        }

        running = false;

        try
        {
            source?.Close();
        }
        catch
        {
            // As above: the input is gone either way.
        }
    }
}
