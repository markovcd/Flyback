using Flyback.Core;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One conversation, written out to a file when somebody asked for that.
/// </summary>
/// <remarks>
/// Off by default: nothing new leaves the machine, but a standing file of it is a
/// different kind of exposure from a transcript that closes with the window, and that
/// is somebody's choice to make. One file per conversation rather than one growing
/// file, named for when it started and who it was with. Turned off, this writes
/// nothing and opens nothing.
/// </remarks>
public sealed class ConversationLog : IDisposable
{
    private readonly StreamWriter? writer;

    private ConversationLog(StreamWriter? writer) => this.writer = writer;

    public static string Folder => Path.Combine(GlobalConstants.DataFolder, "conversations");

    /// <summary>
    /// A log for one conversation, or one that writes nothing at all when logging was
    /// not asked for or the file could not be opened. Never throws: a conversation is
    /// not worth failing to hold over a log of it.
    /// </summary>
    /// <param name="folder">Somewhere other than the usual place, for the tests.</param>
    public static ConversationLog Start(bool enabled, string provider, string? folder = null)
    {
        if (!enabled) return new ConversationLog(null);

        try
        {
            var to = folder ?? Folder;

            Directory.CreateDirectory(to);

            // A random tag rather than relying on the clock alone: two
            // conversations with the same provider can start within the same
            // second, and one silently overwriting the other's file is worse
            // than a name a few characters longer.
            var named = string.Concat(provider.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            var tag = Guid.NewGuid().ToString("N")[..8];
            var path = Path.Combine(to, $"{DateTime.Now:yyyy-MM-dd HHmmss} {named} {tag}.log");

            return new ConversationLog(new StreamWriter(path, append: false) { AutoFlush = true });
        }
        catch
        {
            return new ConversationLog(null);
        }
    }

    /// <summary>
    /// One line, timestamped and named for who said it. Silent when nothing is
    /// open, and silent if the write itself fails partway through a run.
    /// </summary>
    public void Write(string who, string text)
    {
        if (writer is null || text.Length == 0) return;

        try
        {
            writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {who}: {text}");
        }
        catch
        {
            // As Start: a conversation already under way is not worth ending
            // over a line that would not write.
        }
    }

    public void Dispose() => writer?.Dispose();
}
