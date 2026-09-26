using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Something that can be asked for a patch, before any conversation exists. Kept
/// apart from <see cref="IPatchSession"/> for the reason
/// <see cref="Audio.IAudioOutput"/> is kept apart from
/// <see cref="Audio.IAudioDevice"/>: the shell lists what is installed without
/// opening a connection.
/// </summary>
public interface IPatchAssistant
{
    /// <summary>Stable identifier, e.g. <c>anthropic</c>. What a setting names.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>Claude</c>.</summary>
    string Name { get; }

    /// <summary>Higher wins when several are installed. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    /// <summary>Where this one's key comes from. The host holds it; see ADR-0034.</summary>
    AssistantCredential Credential { get; }

    /// <summary>
    /// Every setting this provider has, as the form should stand with
    /// <paramref name="values"/> on it.
    /// </summary>
    /// <remarks>
    /// Asked again after every change, so a field may appear, gray out or change
    /// what it says in answer to another. The App draws what comes back and knows
    /// nothing about any of it. A credential is not among them, and there is no
    /// shape one could go in.
    /// </remarks>
    IReadOnlyList<SettingField> Form(SettingValues values);

    /// <summary>
    /// What a run configured this way may be handed, which the host asks because
    /// the host builds the workbench.
    /// </summary>
    AssistantSenses Senses(SettingValues values);

    /// <summary>
    /// Why this configuration cannot run, or null when it can.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a bool, because the answer is usually one the person
    /// can act on: a key that is not set is not the same kind of no as an operating
    /// system that is not this one. Must answer without a network call and without
    /// throwing; a throw is taken as a no.
    /// </remarks>
    string? Unavailable(AssistantConfig config);

    /// <summary>
    /// Begins a conversation over one workbench. The workbench belongs to the
    /// host; the assistant only drives it.
    /// </summary>
    IPatchSession Start(PatchWorkbench workbench, AssistantConfig config);

    /// <summary>
    /// Carries on a conversation that <see cref="IPatchSession.Save"/> wrote down,
    /// over a workbench the host has already put back as it stood — or null where
    /// this provider cannot.
    /// </summary>
    /// <remarks>
    /// Null costs the model its memory and nothing else: the host starts an
    /// ordinary session over the same workbench, so the patch it was building is
    /// still there. Defaulted, so a provider that has never heard of a saved
    /// conversation still loads. What <paramref name="saved"/> holds is this
    /// provider's own and nobody else reads it (ADR-0072).
    /// </remarks>
    IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved) => null;
}