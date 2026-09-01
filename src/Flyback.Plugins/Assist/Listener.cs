namespace Flyback.Plugins.Assist;

/// <summary>
/// Who hears the patch, when anybody does.
/// </summary>
/// <remarks>
/// <para>
/// A bool until there was a provider whose driving model takes a sound. ADR-0047
/// built the ear as a second model because the chat-completions models that
/// listen demand audio on every request and refuse pictures — the sound could
/// not go to the model doing the building, so it went somewhere else and came
/// back as words. That is <see cref="Another"/>, and it is still what most
/// endpoints can manage.
/// </para>
/// <para>
/// <see cref="Itself"/> is the same tool answered by one model instead of two,
/// and the difference is not a detail of plumbing: it changes what the briefing
/// can honestly say the reply <em>is</em>. Second-hand it is one listener's
/// opinion, told nothing, free to disagree. First-hand it is the model's own
/// impression of something it built and hoped for — which is worth more as
/// evidence and less as a check, and the handbook has to say so either way.
/// </para>
/// <para>
/// Which of the two a run gets is read off <see cref="AssistantModel.Hearing"/>
/// for the model doing the building, so no part of the shell ever knows one
/// model name from another — the boundary ADR-0025 drew. A model nobody wrote
/// down is <see cref="Another"/>: not knowing is not the same as knowing it can,
/// and this is the direction where being wrong costs a description rather than
/// every request.
/// </para>
/// </remarks>
public enum Listener
{
    /// <summary>Nobody. There is no <c>listen</c>, and the briefing says as much.</summary>
    None = 0,

    /// <summary>A second model, played the clip on its own and answering in words.</summary>
    Another = 1,

    /// <summary>The model driving the conversation, played the clip itself.</summary>
    Itself = 2,
}
