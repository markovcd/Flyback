using Flyback.Site.Reading;

namespace Flyback.Site.Checking;

/// <summary>What check-submission says of a preset file: refused with a reason, or what the patch says about itself.</summary>
/// <param name="Lacks">What the web pages lack to open it, or null where they open it.</param>
internal sealed record PresetCheck(
    bool Accepted,
    string? Reason = null,
    string? Name = null,
    string? Author = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    BrowserLack? Lacks = null)
{
    public const string NotAPatch = "That is not a Flyback patch. Send a .fbk or .fbkb file.";

    public static PresetCheck Of(string fileName, byte[] file, string? name, BrowserPlugins browser) =>
        Submissions.Read(fileName, file, name) is { } read
            ? new PresetCheck(true, null, read.Name, read.Author, read.Description, read.Tags, browser.Lacking(fileName, file))
            : new PresetCheck(false, NotAPatch);
}
