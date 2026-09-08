using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Core;
using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

/// <summary>
/// What the assistant panel was last set to. The first thing this application
/// has ever written about itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no key in here, and there never will be.</b> Which provider, and
/// whatever that provider asked to be remembered — those are choices, and a
/// choice is worth remembering in a file. A credential is not: it goes to the
/// operating system's own store or nowhere at all. See ADR-0034 and
/// <see cref="Credentials"/>.
/// </para>
/// <para>
/// What a provider's choices <em>are</em> is not written down here, because
/// nothing in the App knows — a provider declares its own settings and this
/// keeps the answers under the names it gave them (ADR-0069). So the file grew
/// one level: a bag of strings per provider, which is also what stops two
/// providers with a setting of the same name from overwriting each other, and
/// what lets somebody keep a configured endpoint on one while trying another.
/// </para>
/// <para>
/// Nothing here is load-bearing. A file that is missing, unreadable or written
/// by a different version means the defaults, because losing a preference is not
/// worth failing to start over.
/// </para>
/// </remarks>
public sealed class AssistantSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Which assistant, by <see cref="IPatchAssistant.Id"/>. Empty means whichever is preferred.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Whether a key typed in should be handed to the operating system to keep.
    /// The key itself is not here — this only records the answer to the question.
    /// </summary>
    /// <remarks>
    /// One answer for every provider rather than one each, because it is an
    /// answer about this machine — whether secrets belong in its store — rather
    /// than about whoever is being talked to.
    /// </remarks>
    public bool RememberKey { get; set; }

    /// <summary>
    /// What each provider was last set to, filed under its id.
    /// </summary>
    /// <remarks>
    /// Plain strings, in the shape a provider's own <see cref="AssistantField"/>
    /// list gave them, so the file stays readable and hand-editable and this
    /// class stays ignorant of what any of it means. A settable property with a
    /// public setter because that is what the serialiser needs; everything in
    /// the program goes through <see cref="Of"/> and <see cref="Remember"/>.
    /// </remarks>
    public Dictionary<string, Dictionary<string, string>> Choices { get; set; } = new(StringComparer.Ordinal);

    public static string File => Path.Combine(GlobalConstants.DataFolder, "assistant.json");

    /// <summary>What is set for one provider, and nothing at all for one nobody has configured.</summary>
    public AssistantValues Of(string provider) =>
        Choices.TryGetValue(provider, out var held) ? new AssistantValues(held) : AssistantValues.None;

    /// <summary>
    /// Takes one provider's answers, leaving every other provider's alone.
    /// </summary>
    /// <remarks>
    /// Kept rather than replaced, so that trying a second provider for an
    /// afternoon does not cost the endpoint and model somebody spent time on for
    /// the first.
    /// </remarks>
    public void Remember(string provider, AssistantValues values) =>
        Choices[provider] = new Dictionary<string, string>(values.All, StringComparer.Ordinal);

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    /// <param name="path">Somewhere other than the usual place, for the tests.</param>
    public static AssistantSettings Load(string? path = null)
    {
        try
        {
            var from = path ?? File;

            return System.IO.File.Exists(from)
                ? JsonSerializer.Deserialize<AssistantSettings>(System.IO.File.ReadAllText(from), Options) ?? new()
                : new AssistantSettings();
        }
        catch
        {
            return new AssistantSettings();
        }
    }

    /// <summary>
    /// Writes the choices out. Throws if it cannot, so the caller can say so —
    /// silently forgetting what somebody just set is worse than a line in the
    /// status bar.
    /// </summary>
    public void Save(string? path = null)
    {
        var to = path ?? File;

        Directory.CreateDirectory(Path.GetDirectoryName(to) ?? GlobalConstants.DataFolder);
        System.IO.File.WriteAllText(to, JsonSerializer.Serialize(this, Options));
    }
}
