using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The settings a provider of the ordinary shape has, and the form that puts them
/// in front of somebody.
/// </summary>
/// <remarks>
/// Shared by both adapters; another provider declares its own
/// <see cref="SettingField"/> list. Keeps <see cref="Form"/> and
/// <see cref="Read"/> in agreement.
/// </remarks>
/// <param name="DefaultModel">What an unconfigured provider starts on.</param>
/// <param name="SuggestedModels">What the model box offers, and what is known about each.</param>
/// <param name="EnvironmentVariable">The variable conventionally holding the key, read by the shell and never the plugin.</param>
/// <param name="CredentialHelp">One line saying where a key comes from, shown under the field.</param>
/// <param name="DefaultBaseUrl">Null when the endpoint is not the caller's business.</param>
/// <param name="BaseUrlEditable">True for an endpoint shape many providers share, such as OpenAI's.</param>
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
    public AssistantSchema Surveyed(SettingValues values)
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
    public IReadOnlyList<SettingField> Form(SettingValues values)
    {
        var model = values.Text(ModelKey, DefaultModel);
        var known = Known(model);
        var ears = Ears.Select(m => new SettingOption(m.Id, m.Id)).ToList();

        var fields = new List<SettingField>
        {
            new SettingField.Pick(
                ModelKey,
                "Model",
                SuggestedModels.Select(m => new SettingOption(m.Id, m.Id)).ToList(),
                DefaultModel,
                Editable: true)
            {
                Note = known is null
                    ? "Nothing is known about this one here, so the switch below is yours to set. An "
                      + "endpoint that will not take a picture answers with a 400."
                    : Handles(known),
            },

            new SettingField.Text(EndpointKey, "Endpoint", DefaultBaseUrl ?? string.Empty)
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
            new SettingField.Switch(VisionKey, "Let it look at the picture", On: true)
            {
                Enabled = known?.Vision != false,
                Because = known is null ? null : $"{known.Id} does not take pictures.",
            },

            new SettingField.Switch(HearingKey, "Let it listen to the sound")
            {
                Enabled = ears.Count > 0,
                Because = ears.Count > 0 ? null : "This provider has no model that takes a sound.",
            },
        };

        // A model that takes a sound itself is played the clip directly, so
        // there is no second model and no question to put. The field goes rather
        // than graying out; what it held is still in the settings and comes back
        // the moment a model that cannot hear is chosen.
        if (ears.Count > 0 && known?.Hearing != true)
            fields.Add(new SettingField.Pick(EarKey, "Ear", ears, ears[0].Id)
            {
                Enabled = values.Flag(HearingKey),
                Because = "Nobody is listening, so there is nobody to choose.",
            });

        fields.Add(new SettingField.Pick(
            EffortKey,
            "Effort",
            Enum.GetValues<AssistantEffort>().Select(e => new SettingOption(e.ToString(), e.ToString())).ToList(),
            nameof(AssistantEffort.Medium)));

        return fields;
    }

    /// <summary>
    /// What a survey should ask about, with <see cref="SurveyOptions.Chosen"/>
    /// resolved into the model these settings name.
    /// </summary>
    /// <remarks>
    /// The one translation an ordinary provider owes a caller that cannot know
    /// which of its settings is the model. Everything else is passed through,
    /// including a list somebody named by hand — asking for both is asking for
    /// the chosen one.
    /// <para>
    /// Through <see cref="Surveyed"/> and <see cref="Read"/>, as the form is, so
    /// the model this names is the one the box shows: with a survey written down
    /// and no model picked, the two defaults are not the same name.
    /// </para>
    /// </remarks>
    public SurveyOptions Asking(SurveyOptions options, SettingValues values) =>
        options.Chosen
            ? options with { Only = [Surveyed(values).Read(values).Model], All = false }
            : options;

    /// <summary>
    /// What a form filled in this way actually means, which is not quite what it
    /// says.
    /// </summary>
    /// <remarks>
    /// Two settings are held to what the chosen model can do rather than to what the
    /// switch shows: a picture sent to a model recorded as refusing one is a 400,
    /// and listening with nobody to listen is a tool answered with a sentence saying
    /// nobody heard it. <see cref="AssistantChoices.EarModel"/> is null where the model takes a sound
    /// itself, since an ear names the model asked instead.
    /// </remarks>
    public AssistantChoices Read(SettingValues values)
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
    public AssistantSenses Senses(SettingValues values)
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