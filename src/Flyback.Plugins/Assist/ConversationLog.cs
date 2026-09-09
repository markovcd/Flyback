using Flyback.Core;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One conversation, written out to a file when somebody asked for that.
/// </summary>
/// <remarks>
/// <para>
/// Off by default, because what goes through here is the same instruction and
/// the same summaries a turn already sends to whichever provider is configured
/// — nothing new leaves the machine — but a standing file of it is a different
/// kind of exposure than a transcript that closes with the window and is gone,
/// and that is somebody's choice to make, not this program's. See
/// <see cref="AssistantSettings.LogConversations"/>.
/// </para>
/// <para>
/// One file per conversation rather than one growing file, named for when it
/// started and who it was with, so a bad run can be found and read without
/// scrolling past every other one. Turned off, this writes nothing and opens
/// nothing — there is no file to forget to clean up.
/// </para>
/// </remarks>
public sealed class ConversationLog : IDisposable
{
    private readonly StreamWriter? writer;

    private ConversationLog(StreamWriter? writer) => this.writer = writer;

    public static string Folder => Path.Combine(GlobalConstants.DataFolder, "conversations");

    /// <summary>
    /// A log for one conversation, or one that writes nothing at all when
    /// logging was not asked for or the file could not be opened.
    /// </summary>
    /// <remarks>
    /// Never throws. A conversation is not worth failing to hold over a log of
    /// it — the same reasoning <see cref="AssistantSettings.Load"/> already
    /// applies to the settings file this flag lives in.
    /// </remarks>
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
