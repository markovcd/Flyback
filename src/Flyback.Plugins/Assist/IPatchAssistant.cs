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
/// One model a provider suggests, and what it will accept being handed.
/// </summary>
/// <remarks>
/// A suggestion, not a whitelist: anything may be typed, because
/// <see cref="AssistantSchema.BaseUrlEditable"/> means the endpoint may be one
/// nobody here has heard of. Nothing outside a plugin reads this — the shell must
/// not know one model name from another (ADR-0069).
/// </remarks>
/// <param name="Id">What goes in the request.</param>
/// <param name="Vision">Whether it accepts a picture. Nearly all of them do.</param>
/// <param name="Hearing">
/// Whether it accepts a sound. Most do not — see
/// <see cref="AssistantChoices.Hearing"/>. True and <paramref name="Vision"/>
/// true is the case everything downstream turns on: such a model drives the
/// conversation and is played the clip itself, so the run has no second model and
/// no <see cref="AssistantChoices.EarModel"/>.
/// </param>
public sealed record AssistantModel(string Id, bool Vision = true, bool Hearing = false);

/// <summary>
/// The settings a provider of the ordinary shape has, and the form that puts them
/// in front of somebody.
/// </summary>
/// <remarks>
/// A helper on the plugin's side of the boundary. Both adapters here ask the same
/// five questions, so the declaration is written once and delegated to; a provider
/// of some other shape declares its own <see cref="AssistantField"/> list instead.
/// It is also the one place that knows both directions of a setting — what the
/// form offers (<see cref="Form"/>) and what a configured run means
/// (<see cref="Read"/>) — which have to agree, since a switch shown for a model
/// that refuses pictures would be a switch that lies.
/// </remarks>
/// <param name="DefaultModel">What a provider nobody has configured starts on.</param>
/// <param name="SuggestedModels">What the model box offers, and what is known about each.</param>
/// <param name="EnvironmentVariable">
/// The variable this provider is conventionally given its key in. The shell reads
/// it, not the plugin: a plugin that went looking for a credential could keep one.
/// </param>
/// <param name="CredentialHelp">One line saying where a key comes from, shown under the field.</param>
/// <param name="DefaultBaseUrl">Null when the endpoint is not the caller's business.</param>
/// <param name="BaseUrlEditable">
/// True only where pointing somewhere else is the point — an OpenAI-shaped
/// endpoint reaches a dozen providers and a local runtime besides.
/// </param>
public sealed record AssistantSchema(
    string DefaultModel,
    IReadOnlyList<AssistantModel> SuggestedModels,
    string EnvironmentVariable,
    string CredentialHelp,
    string? DefaultBaseUrl = null,
    bool BaseUrlEditable = false)
{
    /// <summary>
    /// What each setting is filed under. Public because a plugin that borrows this
    /// form reads its own values back by the same names, and stable because they
    /// are in the settings file of everybody who has configured one.
    /// </summary>
    public const string ModelKey = "model";

    public const string EndpointKey = "endpoint";

    public const string VisionKey = "vision";

    public const string HearingKey = "hearing";

    public const string EarKey = "ear";

    public const string EffortKey = "effort";

    /// <summary>What this provider says about the key it needs.</summary>
    public AssistantCredential Credential => new(EnvironmentVariable, CredentialHelp);

    /// <summary>
    /// What is known about the model somebody has typed, or null when it is not one
    /// of these. Null is not "cannot" but "nobody here knows", which is the
    /// ordinary state of a model at an endpoint somebody pointed at by hand — so it
    /// leaves every switch where it was.
    /// </summary>
    public AssistantModel? Known(string? model) =>
        string.IsNullOrWhiteSpace(model)
            ? null
            : SuggestedModels.Where(m => IsOne(model, m.Id)).MaxBy(m => m.Id.Length);

    /// <summary>
    /// Every model here that will take a sound, in the order the provider listed
    /// them — what the form offers as an ear, and empty for a provider that has
    /// none.
    /// </summary>
    public IEnumerable<AssistantModel> Ears => SuggestedModels.Where(m => m.Hearing);

    /// <summary>
    /// This schema as a survey of the endpoint leaves it, or unchanged where nobody
    /// has run one.
    /// </summary>
    /// <remarks>
    /// A survey replaces the suggestions rather than joining them: a suggestion is
    /// what somebody believed when they wrote the line, and a survey is what the
    /// endpoint said when asked. The default moves with them, since a written-down
    /// default the survey did not find would hand a fresh window the one model known
    /// not to work.
    /// </remarks>
    public AssistantSchema Surveyed(AssistantValues values)
    {
        var found = Survey.Read(values.Text(Survey.Key, string.Empty));

        if (found.Count == 0) return this;

        var models = found.Select(m => m.Suggestion).ToList();

        var kept = models.Any(m => string.Equals(m.Id, DefaultModel, StringComparison.OrdinalIgnoreCase));

        return this with
        {
            SuggestedModels = models,
            DefaultModel = kept ? DefaultModel : models[0].Id,
        };
    }

    /// <summary>
    /// The form as it stands, given what is set on it so far.
    /// </summary>
    /// <remarks>
    /// Computed rather than held, because half of what is on it depends on the
    /// rest: the model decides whether looking is offered and whether there is a
    /// second model to choose. The App asks again after every change.
    /// </remarks>
    public IReadOnlyList<AssistantField> Form(AssistantValues values)
    {
        var model = values.Text(ModelKey, DefaultModel);
        var known = Known(model);
        var ears = Ears.Select(m => new AssistantOption(m.Id, m.Id)).ToList();

        var fields = new List<AssistantField>
        {
            new AssistantField.Pick(
                ModelKey,
                "Model",
                SuggestedModels.Select(m => new AssistantOption(m.Id, m.Id)).ToList(),
                DefaultModel,
                Editable: true)
            {
                Note = known is null
                    ? "Nothing is known about this one here, so the switch below is yours to set. An "
                      + "endpoint that will not take a picture answers with a 400."
                    : Handles(known),
            },

            new AssistantField.Text(EndpointKey, "Endpoint", DefaultBaseUrl ?? string.Empty)
            {
                Enabled = BaseUrlEditable,
                Because = BaseUrlEditable ? null : "This format is spoken in one place.",
            },

            // The tick is left alone whatever the model refuses, and that is
            // deliberate: a disabled box that keeps its state is a preference
            // parked, and one that clears itself is a preference destroyed by
            // passing through a model on the way to another. Read is what makes
            // it safe — it sends no picture to a model recorded as refusing one,
            // whatever the box still shows.
            new AssistantField.Switch(VisionKey, "Let it look at the picture", On: true)
            {
                Enabled = known?.Vision != false,
                Because = known is null ? null : $"{known.Id} does not take pictures.",
            },

            new AssistantField.Switch(HearingKey, "Let it listen to the sound")
            {
                Enabled = ears.Count > 0,
                Because = ears.Count > 0 ? null : "This provider has no model that takes a sound.",
            },
        };

        // A model that takes a sound itself is played the clip directly, so
        // there is no second model and no question to put. The field goes rather
        // than greying out; what it held is still in the settings and comes back
        // the moment a model that cannot hear is chosen.
        if (ears.Count > 0 && known?.Hearing != true)
            fields.Add(new AssistantField.Pick(EarKey, "Ear", ears, ears[0].Id)
            {
                Enabled = values.Flag(HearingKey),
                Because = "Nobody is listening, so there is nobody to choose.",
            });

        fields.Add(new AssistantField.Pick(
            EffortKey,
            "Effort",
            Enum.GetValues<AssistantEffort>().Select(e => new AssistantOption(e.ToString(), e.ToString())).ToList(),
            nameof(AssistantEffort.Medium)));

        return fields;
    }

    /// <summary>
    /// What a form filled in this way actually means, which is not quite what it
    /// says.
    /// </summary>
    /// <remarks>
    /// Two settings are held to what the chosen model can do rather than to what the
    /// switch shows: a picture sent to a model recorded as refusing one is a 400,
    /// and listening with nobody to listen is a tool answered with a sentence saying
    /// nobody heard it. <see cref="EarModel"/> is null where the model takes a sound
    /// itself, since an ear names the model asked instead.
    /// </remarks>
    public AssistantChoices Read(AssistantValues values)
    {
        var model = values.Text(ModelKey, DefaultModel);
        var known = Known(model);
        var endpoint = values.Text(EndpointKey, DefaultBaseUrl ?? string.Empty);

        var ear = known?.Hearing == true
            ? null
            : Blank(values.Text(EarKey, Ears.FirstOrDefault()?.Id ?? string.Empty));

        return new AssistantChoices(
            model,
            Blank(endpoint) ?? DefaultBaseUrl,
            values.Flag(VisionKey, true) && known?.Vision != false,
            values.Flag(HearingKey) && (ear is not null || known?.Hearing == true),
            ear,
            values.Word(EffortKey, AssistantEffort.Medium));
    }

    /// <summary>What a run configured this way may be handed.</summary>
    /// <remarks>
    /// Whose ear it is falls out of the two facts the form was built from: listening
    /// has to be on, and the model either takes a sound or does not. A model nobody
    /// wrote down falls to the second-hand arrangement, which is the safe direction:
    /// being wrong that way costs a description, and being wrong the other way loses
    /// every turn from the first <c>listen</c> onwards.
    /// </remarks>
    public AssistantSenses Senses(AssistantValues values)
    {
        var chosen = Read(values);

        return new AssistantSenses(
            chosen.Vision,
            !chosen.Hearing ? Listener.None
            : Known(chosen.Model)?.Hearing == true ? Listener.Itself
            : Listener.Another);
    }

    /// <summary>
    /// One line naming what the model doing the building accepts. It names the
    /// model it matched rather than what was typed, so a dated snapshot says
    /// which family it was read as — see <see cref="Known"/>.
    /// </summary>
    private static string Handles(AssistantModel model) => (model.Vision, model.Hearing) switch
    {
        // The one that needs saying, because it is what removes the ear below
        // and somebody who had chosen one deserves to know where it went.
        (true, true) => $"{model.Id} takes pictures and sound, so it listens for itself — there is "
            + "no second model to choose.",

        (true, false) => $"{model.Id} takes pictures.",

        // Worth saying rather than leaving somebody to wonder why they chose an
        // audio model and lost the ability to look at anything: this one belongs
        // in the ear below, where the sound actually goes.
        (false, true) => $"{model.Id} is a listener — it takes sound and not pictures, which is what "
            + "the ear below is for. Driving with it works, but it builds blind.",

        _ => $"{model.Id} takes no pictures, so it builds from the compiler alone.",
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Whether a typed name is one of ours: the name itself, or that name with a
    /// date or a version after it.
    /// </summary>
    /// <remarks>
    /// The suffix has to begin with a digit, and that is the whole rule.
    /// <c>gpt-4o-transcribe</c> begins with <c>gpt-4o</c> and is a different model
    /// with different capabilities, where <c>gpt-4o-2024-11-20</c> is the same one
    /// pinned to a day. Where two could match the longer wins, so a dated mini reads
    /// as a mini.
    /// </remarks>
    private static bool IsOne(string typed, string id)
    {
        if (typed.Equals(id, StringComparison.OrdinalIgnoreCase)) return true;
        if (!typed.StartsWith(id, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = typed.AsSpan(id.Length);

        return rest.Length > 1 && rest[0] == '-' && char.IsAsciiDigit(rest[1]);
    }
}

/// <summary>
/// A filled-in form of the ordinary shape, read back as the things it decides —
/// what <see cref="AssistantSchema.Read"/> makes of a set of values. Nothing
/// outside a plugin sees one.
/// </summary>
/// <param name="Vision">Whether the model may be shown a rendered frame.</param>
/// <param name="Hearing">
/// Whether the patch's sound may be listened to at all. Off by default, and the
/// asymmetry with <paramref name="Vision"/> is the point: every model this reaches
/// can be shown a picture, and only some can be played a sound.
/// </param>
/// <param name="EarModel">
/// The model asked to listen instead of the one doing the building, or null where
/// no second model is wanted.
/// <para>
/// Null carries two situations the adapter tells apart from its own schema. Where
/// the driving model takes a sound, there is nobody else to ask and the clip goes
/// into the conversation; where it does not, nothing has been chosen and
/// <paramref name="Hearing"/> has nothing to act on.
/// </para>
/// <para>
/// A second model is the older arrangement and still the common one — ADR-0047
/// records why the chat-completions format forces it.
/// </para>
/// </param>
public sealed record AssistantChoices(
    string Model,
    string? BaseUrl = null,
    bool Vision = true,
    bool Hearing = false,
    string? EarModel = null,
    AssistantEffort Effort = AssistantEffort.Medium);

/// <summary>
/// One configured provider, ready to be asked something.
/// </summary>
/// <remarks>
/// The two halves are kept apart because they are owned by different sides:
/// <see cref="Values"/> is what the provider asked for and reads back, and
/// <see cref="ApiKey"/> is the host's and lives no longer than the run — never in
/// the settings file, never logged (ADR-0034).
/// </remarks>
/// <param name="Values">Every setting this provider declared, as it stands.</param>
public sealed record AssistantConfig(string ApiKey, AssistantValues Values)
{
    /// <summary>Nothing configured, which is what a provider is asked about before anybody has.</summary>
    public static AssistantConfig Unset { get; } = new(string.Empty, AssistantValues.None);
}

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
    /// Asked again after every change, so a field may appear, grey out or change
    /// what it says in answer to another. The App draws what comes back and knows
    /// nothing about any of it. A credential is not among them, and there is no
    /// shape one could go in.
    /// </remarks>
    IReadOnlyList<AssistantField> Form(AssistantValues values);

    /// <summary>
    /// What a run configured this way may be handed, which the host asks because
    /// the host builds the workbench.
    /// </summary>
    AssistantSenses Senses(AssistantValues values);

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
}

/// <summary>
/// A conversation in progress. Multi-turn on purpose: the second instruction —
/// "more blue, and slower" — is the common one, and it should keep both the
/// history and whatever prompt cache the provider built for the first.
/// </summary>
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
