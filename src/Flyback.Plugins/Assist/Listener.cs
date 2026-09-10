namespace Flyback.Plugins.Assist;

/// <summary>
/// Who hears the patch, when anybody does.
/// </summary>
/// <remarks>
/// A bool until there was a provider whose driving model takes a sound. ADR-0047
/// built the ear as a second model because the chat-completions models that listen
/// demand audio on every request and refuse pictures; that is
/// <see cref="Another"/>, and it is still what most endpoints can manage.
/// <para>
/// <see cref="Itself"/> is the same tool answered by one model instead of two, and
/// it changes what the briefing can honestly say the reply is: second-hand it is
/// one listener's opinion, told nothing; first-hand it is the model's own
/// impression of something it built and hoped for.
/// </para>
/// <para>
/// Which a run gets is read off <see cref="AssistantModel.Hearing"/>, so no part of
/// the shell knows one model name from another (ADR-0025). A model nobody wrote
/// down is <see cref="Another"/>: being wrong that way costs a description rather
/// than every request.
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
