using System.Text.Json.Nodes;
using Flyback.App.Assist;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Assist;

/// <summary>
/// Conversations kept for patch files, found again by the file and only while the
/// file is what it was saved as.
/// </summary>
/// <remarks>
/// No patch file is ever written: what is kept is found by a path and a text, and
/// neither has to exist on the disk for that.
/// </remarks>
public class ConversationStoreTests : IDisposable
{
    private const string Text = """{"nodes":[]}""";

    private const string Conversation = """{"shape":1,"provider":"gemini"}""";

    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "flyback-sessions-" + Guid.NewGuid().ToString("N"));

    private readonly string patch = Path.Combine(
        Path.GetTempPath(), "flyback-patches-" + Guid.NewGuid().ToString("N"), "techno.fbk");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        if (File.Exists(folder)) File.Delete(folder);
    }

    [Fact]
    public void What_is_kept_for_a_file_is_found_for_it()
    {
        var store = new ConversationStore(folder);

        store.Keep(patch, Text, Conversation).ShouldBeTrue();

        JsonNode.DeepEquals(JsonNode.Parse(store.Find(patch, Text)!), JsonNode.Parse(Conversation))
            .ShouldBeTrue();
    }

    [Fact]
    public void Nothing_kept_is_nothing_found() =>
        new ConversationStore(folder).Find(patch, Text).ShouldBeNull();

    /// <summary>
    /// Changed somewhere else — another program, a checkout — and so not the patch
    /// the conversation was about. A single character is enough.
    /// </summary>
    [Fact]
    public void A_file_that_has_changed_since_is_not_the_one_it_was_about()
    {
        var store = new ConversationStore(folder);

        store.Keep(patch, Text, Conversation);

        store.Find(patch, Text + " ").ShouldBeNull();
    }

    [Fact]
    public void Another_file_with_the_same_text_finds_nothing()
    {
        var store = new ConversationStore(folder);

        store.Keep(patch, Text, Conversation);

        store.Find(Path.Combine(Path.GetDirectoryName(patch)!, "bass.fbk"), Text).ShouldBeNull();
    }

    /// <summary>
    /// A file saved over with a patch that has no conversation must not open next
    /// time with the one the last patch had — even when the two patches read the same.
    /// </summary>
    [Fact]
    public void Saving_a_file_with_no_conversation_forgets_the_one_it_had()
    {
        var store = new ConversationStore(folder);

        store.Keep(patch, Text, Conversation);
        store.Keep(patch, Text, null).ShouldBeTrue();

        store.Find(patch, Text).ShouldBeNull();
    }

    [Fact]
    public void Forgetting_what_was_never_kept_is_not_a_failure() =>
        new ConversationStore(folder).Keep(patch, Text, null).ShouldBeTrue();

    /// <summary>
    /// The patch is on disk by the time this is asked, so a folder that cannot be
    /// made is a refusal to report rather than a save that failed.
    /// </summary>
    [Fact]
    public void A_folder_that_cannot_be_made_is_a_refusal_rather_than_a_throw()
    {
        File.WriteAllText(folder, "something is already here");

        new ConversationStore(folder).Keep(patch, Text, Conversation).ShouldBeFalse();
    }

    [Fact]
    public void The_same_file_named_in_another_case_is_found_where_case_does_not_matter()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new ConversationStore(folder);

        store.Keep(patch, Text, Conversation);

        store.Find(patch.ToUpperInvariant(), Text).ShouldNotBeNull();
    }
}
