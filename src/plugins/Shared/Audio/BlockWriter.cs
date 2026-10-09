using Flyback.Core;

namespace Flyback.Plugins.Audio;

/// <summary>
/// The audio thread of a device whose writes block until it has room: fills a block and
/// writes it until stopped or refused, then closes the device itself.
/// </summary>
/// <remarks>
/// The thread holds the only references to its block, its callback and its device, so a
/// thread that outlives <see cref="Stop"/> can never reach a later writer's.
/// </remarks>
internal sealed class BlockWriter
{
    /// <summary>Long enough that a slow device is not taken for one that has stopped answering.</summary>
    private const int ExitMilliseconds = 2000;

    private readonly Thread thread;
    private readonly float[] block;
    private readonly AudioCallback fill;
    private readonly Func<float[], Func<bool>, bool> write;
    private readonly Action close;
    private readonly Func<bool> wanted;
    private volatile bool running = true;

    private BlockWriter(float[] block, AudioCallback fill, Func<float[], Func<bool>, bool> write, Action close)
    {
        (this.block, this.fill, this.write, this.close) = (block, fill, write, close);
        wanted = () => running;
        thread = new Thread(Run) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} audio" };
    }

    /// <summary>Whether the thread is still filling and writing.</summary>
    public bool IsRunning => running;

    /// <summary>
    /// Starts the thread. <paramref name="write"/> writes the whole block, asking the
    /// function it is given whether to carry on between partial writes, and returns false
    /// once the device is gone. <paramref name="close"/> runs on the thread as it ends.
    /// </summary>
    public static BlockWriter Start(float[] block, AudioCallback fill, Func<float[], Func<bool>, bool> write, Action close)
    {
        var writer = new BlockWriter(block, fill, write, close);
        writer.thread.Start();
        return writer;
    }

    /// <summary>
    /// Asks the thread to end and waits for it to close the device. A device that has
    /// stopped answering is left to its thread, which closes it if the write ever returns.
    /// </summary>
    public void Stop(int waitMilliseconds = ExitMilliseconds)
    {
        running = false;
        thread.Join(waitMilliseconds);
    }

    private void Run()
    {
        try
        {
            while (running)
            {
                fill(block);

                if (!write(block, wanted)) break;
            }
        }
        catch
        {
            // An exception leaving this thread would end the process rather than the sound.
        }

        running = false;

        try
        {
            close();
        }
        catch
        {
            // As above: the device is gone either way.
        }
    }
}
