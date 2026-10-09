using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Core;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// What the assistant panel was last set to. The first thing this application has
/// ever written about itself.
/// </summary>
/// <remarks>
/// <b>There is no key in here, and there never will be.</b> Which provider, and
/// whatever that provider asked to be remembered, are choices; a credential is not,
/// and goes to the operating system's own store or nowhere (ADR-0034).
/// <para>
/// What a provider's choices are is not written down here, because nothing in the
/// App knows (ADR-0069) — so the file is a bag of strings per provider, which also
/// stops two providers with a setting of the same name from overwriting each
/// other. Nothing here is load-bearing: an unreadable file means the defaults.
/// </para>
/// </remarks>
internal sealed class AssistantSettings
{
    /// <remarks>
    /// Escaped for a person rather than for a web page. The default encoder is
    /// the cautious one and spells anything it is unsure of as an escape, which
    /// is correct anywhere and legible nowhere; these are settings somebody
    /// opens and reads. What is structured does not come through here at all —
    /// see <see cref="ChoiceConverter"/> — so this is about the ordinary values
    /// beside it, an endpoint or a model name.
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new ChoiceConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Which assistant, by <see cref="IPatchAssistant.Id"/>. Empty means none.</summary>
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
    /// Whether every turn is written out to a file under
    /// <see cref="ConversationLog.Folder"/>, one file per conversation. Off until
    /// somebody turns it on — a choice about this machine rather than about any
    /// provider, since the file is written regardless of who was asked.
    /// </summary>
    public bool LogConversations { get; set; }

    /// <summary>Whether the conversation shows the briefing the assistant is handed.</summary>
    public bool ShowBriefing { get; set; } = true;

    /// <summary>Whether the conversation shows the handbook text the assistant looks up.</summary>
    public bool ShowLookups { get; set; } = true;

    /// <summary>What <see cref="ContextLimit"/> is until somebody changes it: under the smallest window a shipped provider offers.</summary>
    public const int DefaultContextLimit = 100_000;

    /// <summary>The fewest and most tokens <see cref="ContextLimit"/> may be set to. The briefing alone is over 20k.</summary>
    public const int LeastContext = 40_000, MostContext = 1_000_000;

    /// <summary>
    /// How many tokens one request may send before the conversation has to be
    /// started again. Every request carries the whole conversation, so this caps
    /// what one can cost: a choice about this machine's account rather than about
    /// any provider. A provider that reports no tokens is held to an estimate.
    /// </summary>
    public int ContextLimit { get; set; } = DefaultContextLimit;

    /// <summary>What <see cref="ProseBudget"/> is until somebody changes it.</summary>
    public const int DefaultProseBudget = 100_000;

    /// <summary>The least and most characters <see cref="ProseBudget"/> may be set to.</summary>
    public const int LeastProse = 10_000, MostProse = 1_000_000;

    /// <summary>
    /// How many characters the briefing may run to before module descriptions
    /// start being left out of it — see <see cref="ProsePolicy"/>. Every request
    /// in a conversation carries the whole briefing, so this is a cap on what each
    /// one costs, and on how much of a small model's context it takes up.
    /// </summary>
    public int ProseBudget { get; set; } = DefaultProseBudget;

    /// <summary>
    /// What each provider was last set to, filed under its id.
    /// </summary>
    /// <remarks>
    /// Plain strings, in the shape a provider's own <see cref="SettingField"/> list
    /// gave them, so the file stays hand-editable and this class stays ignorant of
    /// what any of it means. Public setter because that is what the serialiser
    /// needs; everything else goes through <see cref="Of"/> and
    /// <see cref="Remember"/>. Strings here and not necessarily strings in the file —
    /// see <see cref="ChoiceConverter"/>.
    /// </remarks>
    public Dictionary<string, Dictionary<string, string>> Choices { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Where these settings are kept in <see cref="SettingsFile"/>.</summary>
    public const string Section = "assistant";

    /// <summary>Where the priority list is read from for the settings at <paramref name="path"/>: beside them.</summary>
    public static string PriorityFileBeside(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileName(PriorityModules.File));

    /// <summary>
    /// The budget and the priority list as they stand, for the settings at
    /// <paramref name="path"/>; the shipped list where they are kept in memory.
    /// The list is read from its file every time, so an edit counts from the next conversation.
    /// </summary>
    public ProsePolicy Prose(string? path) => new(
        ProseBudget,
        path is null ? PriorityModules.Parse(PriorityModules.Shipped) : PriorityModules.Load(PriorityFileBeside(path)));

    /// <summary>What is set for one provider, and nothing at all for one nobody has configured.</summary>
    public SettingValues Of(string provider) =>
        Choices.TryGetValue(provider, out var held) ? new SettingValues(held) : SettingValues.None;

    /// <summary>
    /// Takes one provider's answers, leaving every other provider's alone.
    /// </summary>
    /// <remarks>
    /// Kept rather than replaced, so that trying a second provider for an
    /// afternoon does not cost the endpoint and model somebody spent time on for
    /// the first.
    /// </remarks>
    public void Remember(string provider, SettingValues values) =>
        Choices[provider] = new Dictionary<string, string>(values.All, StringComparer.Ordinal);

    /// <summary>Never throws. A settings file is not worth a failure to start.</summary>
    /// <param name="path">Somewhere other than the usual place, for the tests.</param>
    public static AssistantSettings Load(string? path = null)
    {
        try
        {
            var from = path ?? SettingsFile.Path;

            var settings = SettingsFile.Read(from, Section) is { } json
                ? JsonSerializer.Deserialize<AssistantSettings>(json, Options) ?? new()
                : new AssistantSettings();

            // A file edited by hand to nought would leave a conversation that
            // cannot be started at all.
            settings.ContextLimit = Math.Clamp(settings.ContextLimit, LeastContext, MostContext);
            settings.ProseBudget = Math.Clamp(settings.ProseBudget, LeastProse, MostProse);

            return settings;
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
        var to = path ?? SettingsFile.Path;

        SettingsFile.Write(to, Section, JsonSerializer.Serialize(this, Options));
    }
}
