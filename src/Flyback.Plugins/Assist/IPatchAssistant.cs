namespace Flyback.Plugins.Assist;

/// <summary>How hard an assistant should think before answering.</summary>
/// <remarks>
/// Three words rather than a number, because every provider spells this
/// differently and some do not offer it at all. An adapter maps what it can and
/// ignores the rest.
/// </remarks>
public enum AssistantEffort
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// What a provider needs configured, so the shell can put a form in front of
/// someone without knowing which provider it is talking about.
/// </summary>
/// <param name="EnvironmentVariable">
/// The variable this provider is conventionally given its key in. The shell
/// reads it, not the plugin: a credential is the host's to hold, and a plugin
/// that went looking for one would be a plugin that could keep it.
/// </param>
/// <param name="CredentialHelp">One line saying where a key comes from, shown under the field.</param>
/// <param name="DefaultBaseUrl">Null when the endpoint is not the caller's business.</param>
/// <param name="BaseUrlEditable">
/// True only where pointing somewhere else is the point — an OpenAI-shaped
/// endpoint reaches a dozen providers and a local runtime besides.
/// </param>
public sealed record AssistantSchema(
    string DefaultModel,
    IReadOnlyList<string> SuggestedModels,
    string EnvironmentVariable,
    string CredentialHelp,
    string? DefaultBaseUrl = null,
    bool BaseUrlEditable = false);

/// <summary>
/// One configured provider, ready to be asked something.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is the only secret in the contract and it lives no
/// longer than the run. It is never written to the settings file, never logged,
/// and never repeated back in a message — see ADR-0034.
/// </remarks>
public sealed record AssistantConfig(
    string ApiKey,
    string Model,
    string? BaseUrl = null,
    bool Vision = true,
    AssistantEffort Effort = AssistantEffort.Medium);

/// <summary>
/// Something that can be asked for a patch, before any conversation exists.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="IPatchSession"/> for the reason
/// <see cref="Audio.IAudioOutput"/> is kept apart from
/// <see cref="Audio.IAudioDevice"/>: the shell lists what is installed, and says
/// so in the status bar, without opening a connection or spending anything.
/// </remarks>
public interface IPatchAssistant
{
    /// <summary>Stable identifier, e.g. <c>anthropic</c>. What a setting names.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>Claude</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several are installed. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    AssistantSchema Schema { get; }

    /// <summary>
    /// Why this configuration cannot run, or null when it can.
    /// </summary>
    /// <remarks>
    /// A sentence rather than <see cref="Audio.IAudioOutput.IsSupported"/>'s
    /// bool, because the answer here is usually one the person can act on — a
    /// key that is not set is not the same kind of no as an operating system
    /// that is not this one. Must answer without a network call and without
    /// throwing; a throw is taken as a no.
    /// </remarks>
    string? Unavailable(AssistantConfig config);

    /// <summary>
    /// Begins a conversation over one workbench. The workbench belongs to the
    /// host; the assistant only drives it.
    /// </summary>
    IPatchSession Start(PatchWorkbench workbench, AssistantConfig config);
}

/// <summary>
/// A conversation in progress.
/// </summary>
/// <remarks>
/// Multi-turn on purpose. The second instruction — "more blue, and slower" — is
/// the common one, and it should keep both the history and whatever prompt cache
/// the provider built for the first.
/// </remarks>
public interface IPatchSession : IDisposable
{
    /// <summary>
    /// One turn. Yields as work happens and completes when the assistant stops.
    /// </summary>
    /// <remarks>
    /// Never throws for a provider failure — that is a
    /// <see cref="PatchEvent.Failed"/>, so a bad key or a dropped connection
    /// costs the turn rather than the window. Cancellation ends the sequence and
    /// leaves the workbench wherever it got to, which is safe because the
    /// workbench is a copy.
    /// </remarks>
    IAsyncEnumerable<PatchEvent> Ask(string instruction, CancellationToken cancel);
}
