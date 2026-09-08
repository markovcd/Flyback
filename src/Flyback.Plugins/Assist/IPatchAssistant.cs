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
/// A suggestion, not a whitelist. Anything may be typed, because
/// <see cref="AssistantSchema.BaseUrlEditable"/> means the endpoint may be one
/// nobody here has heard of.
/// <para>
/// Nothing outside a plugin reads this. The shell must not know one model name
/// from another — the boundary ADR-0025 drew, ADR-0033 kept and ADR-0069
/// finished — so what a model can do reaches a person as a field on a form and
/// a sentence under it, both written here.
/// </para>
/// </remarks>
/// <param name="Id">What goes in the request.</param>
/// <param name="Vision">Whether it accepts a picture. Nearly all of them do.</param>
/// <param name="Hearing">
/// Whether it accepts a sound. Most do not — see
/// <see cref="AssistantChoices.Hearing"/>.
/// <para>
/// True <em>and</em> <paramref name="Vision"/> true is the interesting case and
/// the one everything downstream turns on: a model that takes both can drive the
/// conversation and be played the clip itself, so the run has no second model,
/// no <see cref="AssistantChoices.EarModel"/>, and a briefing that says "you can
/// hear" rather than "you have an ear" — see <see cref="Listener"/>.
/// </para>
/// </param>
public sealed record AssistantModel(string Id, bool Vision = true, bool Hearing = false);

/// <summary>
/// The settings a provider of the ordinary shape has, and the form that puts
/// them in front of somebody.
/// </summary>
/// <remarks>
/// <para>
/// A helper on the plugin's side of the boundary rather than something the host
/// reads. Both adapters here have the same five questions — which model, which
/// endpoint, may it look, may it listen, and how hard should it think — so the
/// declaration of them is written once and delegated to, and a provider whose
/// settings are some other shape declares its own
/// <see cref="AssistantField"/> list instead and never touches this.
/// </para>
/// <para>
/// It is also the one place that knows the two directions of a setting: what the
/// form offers (<see cref="Form"/>) and what a configured run means
/// (<see cref="Read"/>). They have to agree — a switch shown for a model that
/// refuses pictures would be a switch that lies — so they are written together
/// and read off the same list of models.
/// </para>
/// </remarks>
/// <param name="DefaultModel">What a provider nobody has configured starts on.</param>
/// <param name="SuggestedModels">What the model box offers, and what is known about each.</param>
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
    IReadOnlyList<AssistantModel> SuggestedModels,
    string EnvironmentVariable,
    string CredentialHelp,
    string? DefaultBaseUrl = null,
    bool BaseUrlEditable = false)
{
    /// <summary>
    /// What each setting is filed under.
    /// </summary>
    /// <remarks>
    /// Public because a plugin that borrows this form has to read its own values
    /// back by the same names, and stable because they are in the settings file
    /// of everybody who has ever configured one of these.
    /// </remarks>
    public const string ModelKey = "model";

    public const string EndpointKey = "endpoint";

    public const string VisionKey = "vision";

    public const string HearingKey = "hearing";

    public const string EarKey = "ear";

    public const string EffortKey = "effort";

    /// <summary>What this provider says about the key it needs.</summary>
    public AssistantCredential Credential => new(EnvironmentVariable, CredentialHelp);

    /// <summary>
    /// What is known about the model somebody has typed, or null when it is not
    /// one of these.
    /// </summary>
    /// <remarks>
    /// Null is not "cannot": it is "nobody here knows", which is the ordinary
    /// state of a model at an endpoint this was pointed at by hand — and it
    /// leaves every switch on the form where it was, rather than taking one
    /// away on a guess.
    /// </remarks>
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
    /// This schema as a survey of the endpoint leaves it, or unchanged where
    /// nobody has run one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A survey replaces the suggestions rather than joining them, because the
    /// two are not the same kind of claim: a suggestion is what somebody
    /// believed when they wrote the line, and a survey is what the endpoint said
    /// when it was asked. Where they disagree about whether a model exists at
    /// all, the endpoint is the one that has to be right — a suggestion that
    /// survived would be a name in the box that answers 404.
    /// </para>
    /// <para>
    /// The default moves with them for that reason. A written-down default the
    /// survey did not find is the exact state a survey exists to get out of, and
    /// leaving it in place would hand a fresh window the one model known not to
    /// work.
    /// </para>
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
    /// rest: the model decides whether looking is even offered and whether there
    /// is a second model to choose, and the tick decides whether choosing one is
    /// live. The App asks again after every change, so each of those answers
    /// arrives as a fresh list rather than as something it had to work out.
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
    /// Two settings are held to what the chosen model can do rather than to what
    /// the switch shows. Sight because a picture sent to a model recorded as
    /// refusing one is a 400 rather than a worse answer, and hearing because
    /// listening with nobody to listen is a tool offered, called, and answered
    /// with a sentence saying nobody heard it.
    /// <para>
    /// <see cref="EarModel"/> is null where the model takes a sound itself, and
    /// null is the whole of how that is said: an ear names the model asked
    /// <em>instead</em>, so leaving one there would have the adapter pay for a
    /// second request per listen that it does not need.
    /// </para>
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
    /// Whose ear it is falls out of the same two facts the form was built from:
    /// listening has to be on, and the model doing the building either takes a
    /// sound or does not. A model nobody wrote down falls to the second-hand
    /// arrangement, which is the safe direction rather than a guess — being
    /// wrong that way costs a description, and being wrong the other way sends a
    /// sound to a model that refuses it and loses every turn from the first
    /// <c>listen</c> onwards.
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
    /// The suffix has to begin with a digit, and that is the whole of the rule.
    /// A bare prefix match is not good enough and the reason is a real one:
    /// <c>gpt-4o-transcribe</c> begins with <c>gpt-4o</c> and is not a
    /// <c>gpt-4o</c> — it is a different model with different capabilities, and
    /// reading it as one would answer a question nobody here can answer, in the
    /// direction that takes a switch away. <c>gpt-4o-2024-11-20</c> is the other
    /// case, and a date is what tells them apart: a word after the name is
    /// another model, a number after it is the same one pinned to a day.
    /// <para>
    /// Where two could match, the longer wins — so a list holding both
    /// <c>gpt-4o</c> and <c>gpt-4o-mini</c> reads a dated mini as a mini.
    /// </para>
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
/// A filled-in form of the ordinary shape, read back as the things it decides.
/// </summary>
/// <remarks>
/// What <see cref="AssistantSchema.Read"/> makes of a set of values, and what an
/// adapter of that shape works from. Nothing outside a plugin sees one: to the
/// App a provider's settings are strings it was told to draw.
/// </remarks>
/// <param name="Vision">Whether the model may be shown a rendered frame.</param>
/// <param name="Hearing">
/// Whether the patch's sound may be listened to at all. Off by default, and the
/// asymmetry with <paramref name="Vision"/> is the point: every model this
/// reaches can be shown a picture, and only some can be played a sound. Who
/// does the listening is <paramref name="EarModel"/>'s question.
/// </param>
/// <param name="EarModel">
/// The model asked to listen <em>instead of</em> the one doing the building, or
/// null where no second model is wanted.
/// <para>
/// Null carries two quite different situations and the adapter can tell them
/// apart from its own schema. Where the driving model takes a sound, null means
/// there is nobody else to ask: the clip goes into the conversation, the way a
/// rendered frame does, and one model both builds and hears. Where it does not,
/// null means nothing has been chosen and <paramref name="Hearing"/> has nothing
/// to act on.
/// </para>
/// <para>
/// A second model is the older arrangement and still the common one — ADR-0047
/// records why it is forced on the chat-completions format. The models there
/// that take a sound require every request to carry one, so a conversation
/// driven by one is refused on its first turn, before anything exists to listen
/// to, and they do not take a picture besides.
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
/// The two halves of a configuration, and they are kept apart because they are
/// owned by different sides. <see cref="Values"/> is what the provider asked for
/// and what it reads back; <see cref="ApiKey"/> is the host's, and it lives no
/// longer than the run — never in the settings file, never logged, never
/// repeated back in a message. See ADR-0034.
/// </remarks>
/// <param name="Values">Every setting this provider declared, as it stands.</param>
public sealed record AssistantConfig(string ApiKey, AssistantValues Values)
{
    /// <summary>Nothing configured, which is what a provider is asked about before anybody has.</summary>
    public static AssistantConfig Unset { get; } = new(string.Empty, AssistantValues.None);
}

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

    /// <summary>Where this one's key comes from. The host holds it; see ADR-0034.</summary>
    AssistantCredential Credential { get; }

    /// <summary>
    /// Every setting this provider has, as the form should stand with
    /// <paramref name="values"/> on it.
    /// </summary>
    /// <remarks>
    /// Asked again after every change, so a field may appear, disappear, grey
    /// out or change what it says about itself in answer to another. The App
    /// draws what comes back and knows nothing about any of it — which is what
    /// lets a provider have settings nobody here imagined, and what keeps model
    /// names out of the shell entirely.
    /// <para>
    /// A credential is not among them, and there is no shape one could go in.
    /// </para>
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
