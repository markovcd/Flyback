using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Assist;

/// <summary>
/// A file that is not written at all until somebody asks for it, and never
/// throws on the way to trying.
/// </summary>
public class ConversationLogTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "flyback-conversations-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Off_writes_nothing_and_opens_nothing()
    {
        var log = ConversationLog.Start(false, "anthropic", folder);

        log.Write("you", "a slow drifting field of blue");

        Directory.Exists(folder).ShouldBeFalse();
    }

    [Fact]
    public void On_writes_what_was_said_and_who_said_it()
    {
        var log = ConversationLog.Start(true, "anthropic", folder);

        log.Write("you", "a slow drifting field of blue");
        log.Write("said", "here is a patch for that");
        log.Dispose();

        var written = File.ReadAllText(Directory.GetFiles(folder).Single());

        written.ShouldContain("you: a slow drifting field of blue");
        written.ShouldContain("said: here is a patch for that");
    }

    /// <summary>
    /// A line that is empty is not worth a timestamp of its own — it would only
    /// be one somebody has to explain later.
    /// </summary>
    [Fact]
    public void An_empty_line_is_not_written()
    {
        var log = ConversationLog.Start(true, "anthropic", folder);

        log.Write("said", string.Empty);
        log.Dispose();

        File.ReadAllText(Directory.GetFiles(folder).Single()).ShouldBeEmpty();
    }

    /// <summary>
    /// Two conversations with the same provider, started in the same second, do
    /// not run together into one file.
    /// </summary>
    [Fact]
    public void Each_conversation_gets_its_own_file()
    {
        var first = ConversationLog.Start(true, "anthropic", folder);
        first.Write("you", "first");
        first.Dispose();

        var second = ConversationLog.Start(true, "anthropic", folder);
        second.Write("you", "second");
        second.Dispose();

        Directory.GetFiles(folder).Length.ShouldBe(2);
    }

    /// <summary>
    /// A provider id becomes a filename, and a filename cannot hold whatever
    /// characters a plugin's own id happens to have.
    /// </summary>
    [Fact]
    public void A_providers_id_lands_in_a_name_the_filesystem_can_take()
    {
        var log = ConversationLog.Start(true, "an/odd:id", folder);
        log.Write("you", "hello");
        log.Dispose();

        Directory.GetFiles(folder).Length.ShouldBe(1);
    }

    /// <summary>
    /// A folder that cannot be created — a file sitting where a directory
    /// belongs — is no worse than logging being off. Starting a conversation
    /// must not be the thing that fails because a setting nobody is looking at
    /// right now could not be honoured.
    /// </summary>
    [Fact]
    public void A_folder_that_cannot_be_made_degrades_to_writing_nothing()
    {
        File.WriteAllText(folder, "something is already here");

        var log = ConversationLog.Start(true, "anthropic", folder);

        Should.NotThrow(() => log.Write("you", "hello"));
    }
}
