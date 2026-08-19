using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

/// <summary>
/// What the assistant panel was last set to. The first thing this application
/// has ever written about itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no key in here, and there never will be.</b> Which provider,
/// which model, which endpoint — those are choices, and a choice is worth
/// remembering in a file. A credential is not: it goes to the operating
/// system's own store or nowhere at all. See ADR-0034 and
/// <see cref="Credentials"/>.
/// </para>
/// <para>
/// Nothing here is load-bearing. A file that is missing, unreadable or written
/// by a later version means the defaults, because losing a preference is not
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

    /// <summary>Empty means the provider's own default.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Only meaningful where the schema says the endpoint is the caller's business.</summary>
    public string? BaseUrl { get; set; }

    public bool Vision { get; set; } = true;

    /// <summary>
    /// Whether the sound may be listened to at all. Off unless somebody says
    /// otherwise, because it spends a second model — see
    /// <see cref="AssistantConfig.Hearing"/>.
    /// </summary>
    public bool Hearing { get; set; }

    /// <summary>
    /// Which model does the listening. Empty means the provider's first one that
    /// can — see <see cref="AssistantConfig.EarModel"/> for why it is not the
    /// model doing the building.
    /// </summary>
    public string EarModel { get; set; } = string.Empty;

    public AssistantEffort Effort { get; set; } = AssistantEffort.Medium;

    /// <summary>
    /// Whether a key typed in should be handed to the operating system to keep.
    /// The key itself is not here — this only records the answer to the question.
    /// </summary>
    public bool RememberKey { get; set; }

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Flyback");

    public static string File => Path.Combine(Folder, "assistant.json");

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

        Directory.CreateDirectory(Path.GetDirectoryName(to) ?? Folder);
        System.IO.File.WriteAllText(to, JsonSerializer.Serialize(this, Options));
    }
}
