using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Gemini;

public sealed partial class GeminiAssistant : IPatchAssistant
{
    public string Id => "gemini";

    public string Name => "Gemini";

    /// <summary>
    /// Below the chat-completions adapter on purpose.
    /// </summary>
    /// <remarks>
    /// Priority decides what a fresh install starts on, and starting somewhere
    /// that needs a key nobody has is worse than starting on the one that has
    /// been there. Anybody who wants this picks it from the list once and the
    /// settings remember it.
    /// </remarks>
    public int Priority => 40;

    /// <summary>
    /// Where somebody starts before anybody has asked the endpoint anything.
    /// </summary>
    /// <remarks>
    /// Flash rather than Pro: it sees, it hears, it thinks, and it is the one
    /// somebody can point at a key from a free tier, which is what makes "bring your
    /// own key" a real offer. Pro is one line away in the box.
    /// <para>
    /// A written-down list is the part that goes stale, and the answer is
    /// <see cref="IModelSurvey"/> — see <see cref="AssistantSchema.Surveyed"/>.
    /// Until one has been run these four are the whole of what is known here, and
    /// any of them may already answer 404, which is why the list is short rather
    /// than exhaustive.
    /// </para>
    /// </remarks>
    public AssistantSchema Schema { get; } = new(
        "gemini-3.6-flash",
        [
            new AssistantModel("gemini-3.1-pro-preview", Hearing: true),
            new AssistantModel("gemini-3.8-flash", Hearing: true),
            new AssistantModel("gemini-3.6-flash", Hearing: true),
            new AssistantModel("gemini-3.5-flash-lite", Hearing: true),
        ],
        "GEMINI_API_KEY",
        "A key from Google AI Studio, restricted in the Cloud console to the Generative "
        + "Language API. The endpoint is fixed — this format is spoken in one place, unlike "
        + "chat completions.",
        "https://generativelanguage.googleapis.com/v1beta");

    /// <summary>
    /// What this one's key comes from. The host holds it; see ADR-0034. A header of its
    /// own rather than the key= parameter the quickstarts use: a secret in a query string
    /// is a secret in every log and proxy between here and there.
    /// </summary>
    public AssistantCredential Credential => Schema.Credential with { Header = "x-goog-api-key", Scheme = null };

    /// <summary>
    /// The ordinary five questions, declared by the schema rather than written out
    /// here — see <see cref="AssistantSchema.Form"/>. The endpoint arrives fixed
    /// rather than absent, because this format is spoken in one place.
    /// </summary>
    /// <remarks>
    /// The model box offers what a survey found, where one has been run, and every
    /// question that reads a model goes through the same substitution: a box
    /// offering surveyed models while <see cref="Senses"/> answered from the
    /// written-down ones would be a form that lies.
    /// </remarks>
    public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Surveyed(values).Form(values);

    public AssistantSenses Senses(SettingValues values) => Schema.Surveyed(values).Senses(values);

    public Uri? Endpoint(SettingValues values) => Schema.Endpoint(values);

    /// <summary>
    /// Answered from the configuration alone — no request, no client, nothing
    /// that costs anything. The endpoint is only found out to be wrong when
    /// somebody actually asks it something, which is the honest moment for it.
    /// </summary>
    public string? Unavailable(AssistantConfig config)
    {
        if (!config.Transport.HasKey)
            return "No key yet — set GEMINI_API_KEY, or put one in Settings.";

        var chosen = Schema.Surveyed(config.Values).Read(config.Values);

        if (string.IsNullOrWhiteSpace(chosen.Model))
            return "No model chosen. Put one in Settings.";

        var endpoint = chosen.BaseUrl ?? Schema.DefaultBaseUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
            return "No endpoint. Put one in Settings.";

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
                ? null
                : $"'{endpoint}' is not an http or https address.";
    }

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => Session(workbench, config);

    public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
    {
        var session = Session(workbench, config);

        if (session.Take(saved)) return session;

        session.Dispose();
        return null;
    }

    private GeminiSession Session(PatchWorkbench workbench, AssistantConfig config)
    {
        var schema = Schema.Surveyed(config.Values);
        var chosen = schema.Read(config.Values);

        return new GeminiSession(
            workbench,
            chosen,
            Schema.DefaultBaseUrl!,
            config.Transport,
            Thinking(config.Values, chosen),
            ownEars: schema.Known(chosen.Model)?.Hearing == true);
    }

    /// <summary>
    /// The three words of effort as this provider spells them, or null where nothing
    /// can safely be said.
    /// </summary>
    /// <remarks>
    /// Medium is dynamic — the model decides for itself, which is what -1 means. Low
    /// and High are the ends of what the chosen model accepts, and those are
    /// per-model numbers that a budget out of range answers with a 400 rather than a
    /// clamp. So they are measured rather than written down, and a model nobody has
    /// measured gets no <c>thinkingConfig</c> at all.
    /// </remarks>
    private static JsonObject? Thinking(SettingValues values, AssistantChoices chosen)
    {
        // Qualified because this class also has a Survey, and the method would
        // otherwise win the name over the type that stores what it found.
        var measured = Assist.Survey.Read(values.Text(Assist.Survey.Key, string.Empty))
            .FirstOrDefault(m => string.Equals(m.Id, chosen.Model, StringComparison.OrdinalIgnoreCase));

        if (measured?.Least is not { } least || measured.Most is not { } most) return null;

        var tokens = chosen.Effort switch
        {
            AssistantEffort.Low => least,
            AssistantEffort.High => most,
            _ => -1,
        };

        return new JsonObject { ["thinkingBudget"] = tokens };
    }

    /// <summary>
    /// Example prompts that worked well with Gemini Flash 3.6 during testing (2026-10-04).
    /// </summary>
    public IReadOnlyList<ExamplePrompt> Examples(string? model = null) =>
        [
            new("Techno preset", TechnoPrompt, "Full techno patch with drums, bass, and visuals"),
            new("Kick and bass", KickBassPrompt, "Simple drum and bass pattern"),
            new("Ambient drone", AmbientPrompt, "Slowly evolving ambient soundscape"),
            new("Visual pattern", VisualPrompt, "Abstract geometric visualization"),
        ];

    // Tested 2026-10-04: Generated 35 modules, 47 wires, 6 panel knobs in 66 seconds
    // with 5 write_patch iterations and automatic error correction.
    private const string TechnoPrompt = """
        Build a techno preset with these requirements:

        AUDIO:
        - Tempo: 130 BPM with a Tempo module
        - Kick drum: Use a Sine oscillator with an envelope shaping both amplitude and frequency (for the pitch drop)
        - Hi-hats: Use a Noise module with an envelope
        - Bass: Saw oscillator with a filter envelope for acid-style sound
        - All drums patterned using Sequencer modules

        PICTURE:
        - Abstract visualization responding to the sound
        - Use Kaleidoscope for visual movement

        PANEL KNOBS:
        1. kick_decay - Kick envelope decay (50ms..500ms)
        2. kick_pitch - Kick base frequency (40..80 Hz)
        3. bass_filter - Filter cutoff (200..2000 Hz)
        4. hat_decay - Hi-hat decay (10ms..200ms)
        5. tempo - Tempo (100..160 BPM)
        6. visual_rotate - Kaleidoscope rotation speed

        Write the complete patch using write_patch in the text language.
        """;

    private const string KickBassPrompt = """
        Build a simple drum and bass pattern:

        AUDIO:
        - Tempo: 120 BPM
        - Kick: Sine oscillator with pitch drop envelope
        - Bass: Saw oscillator with simple envelope
        - Pattern: 4-step sequencer for each

        Write the complete patch using write_patch.
        """;

    private const string AmbientPrompt = """
        Build an ambient drone soundscape:

        AUDIO:
        - Multiple Sine oscillators with detuned frequencies
        - Slow LFO modulation on amplitude
        - Reverb for spaciousness

        PICTURE:
        - Slowly shifting color gradients
        - Clouds pattern module

        Write the complete patch using write_patch.
        """;

    private const string VisualPrompt = """
        Build an abstract geometric visualization:

        PICTURE:
        - Kaleidoscope with rotation
        - Checker or Rings pattern
        - Color mapped to coordinates

        Write the complete patch using write_patch.
        """;
}}

public sealed partial class GeminiAssistant : IPatchAssistant
{
    public string Id => "gemini";

    public string Name => "Gemini";

    /// <summary>
    /// Below the chat-completions adapter on purpose.
    /// </summary>
    /// <remarks>
    /// Priority decides what a fresh install starts on, and starting somewhere
    /// that needs a key nobody has is worse than starting on the one that has
    /// been there. Anybody who wants this picks it from the list once and the
    /// settings remember it.
    /// </remarks>
    public int Priority => 40;

    /// <summary>
    /// Where somebody starts before anybody has asked the endpoint anything.
    /// </summary>
    /// <remarks>
    /// Flash rather than Pro: it sees, it hears, it thinks, and it is the one
    /// somebody can point at a key from a free tier, which is what makes "bring your
    /// own key" a real offer. Pro is one line away in the box.
    /// <para>
    /// A written-down list is the part that goes stale, and the answer is
    /// <see cref="IModelSurvey"/> — see <see cref="AssistantSchema.Surveyed"/>.
    /// Until one has been run these four are the whole of what is known here, and
    /// any of them may already answer 404, which is why the list is short rather
    /// than exhaustive.
    /// </para>
    /// </remarks>
    public AssistantSchema Schema { get; } = new(
        "gemini-3.6-flash",
        [
            new AssistantModel("gemini-3.1-pro-preview", Hearing: true),
            new AssistantModel("gemini-3.8-flash", Hearing: true),
            new AssistantModel("gemini-3.6-flash", Hearing: true),
            new AssistantModel("gemini-3.5-flash-lite", Hearing: true),
        ],
        "GEMINI_API_KEY",
        "A key from Google AI Studio, restricted in the Cloud console to the Generative "
        + "Language API. The endpoint is fixed — this format is spoken in one place, unlike "
        + "chat completions.",
        "https://generativelanguage.googleapis.com/v1beta");

    /// <summary>
    /// What this one's key comes from. The host holds it; see ADR-0034. A header of its
    /// own rather than the key= parameter the quickstarts use: a secret in a query string
    /// is a secret in every log and proxy between here and there.
    /// </summary>
    public AssistantCredential Credential => Schema.Credential with { Header = "x-goog-api-key", Scheme = null };

    /// <summary>
    /// The ordinary five questions, declared by the schema rather than written out
    /// here — see <see cref="AssistantSchema.Form"/>. The endpoint arrives fixed
    /// rather than absent, because this format is spoken in one place.
    /// </summary>
    /// <remarks>
    /// The model box offers what a survey found, where one has been run, and every
    /// question that reads a model goes through the same substitution: a box
    /// offering surveyed models while <see cref="Senses"/> answered from the
    /// written-down ones would be a form that lies.
    /// </remarks>
    public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Surveyed(values).Form(values);

    public AssistantSenses Senses(SettingValues values) => Schema.Surveyed(values).Senses(values);

    public Uri? Endpoint(SettingValues values) => Schema.Endpoint(values);

    /// <summary>
    /// Answered from the configuration alone — no request, no client, nothing
    /// that costs anything. The endpoint is only found out to be wrong when
    /// somebody actually asks it something, which is the honest moment for it.
    /// </summary>
    public string? Unavailable(AssistantConfig config)
    {
        if (!config.Transport.HasKey)
            return "No key yet — set GEMINI_API_KEY, or put one in Settings.";

        var chosen = Schema.Surveyed(config.Values).Read(config.Values);

        if (string.IsNullOrWhiteSpace(chosen.Model))
            return "No model chosen. Put one in Settings.";

        var endpoint = chosen.BaseUrl ?? Schema.DefaultBaseUrl;

        if (string.IsNullOrWhiteSpace(endpoint))
            return "No endpoint. Put one in Settings.";

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
                ? null
                : $"'{endpoint}' is not an http or https address.";
    }

    public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => Session(workbench, config);

    public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
    {
        var session = Session(workbench, config);

        if (session.Take(saved)) return session;

        session.Dispose();
        return null;
    }

    private GeminiSession Session(PatchWorkbench workbench, AssistantConfig config)
    {
        var schema = Schema.Surveyed(config.Values);
        var chosen = schema.Read(config.Values);

        return new GeminiSession(
            workbench,
            chosen,
            Schema.DefaultBaseUrl!,
            config.Transport,
            Thinking(config.Values, chosen),
            ownEars: schema.Known(chosen.Model)?.Hearing == true);
    }

    /// <summary>
    /// The three words of effort as this provider spells them, or null where nothing
    /// can safely be said.
    /// </summary>
    /// <remarks>
    /// Medium is dynamic — the model decides for itself, which is what -1 means. Low
    /// and High are the ends of what the chosen model accepts, and those are
    /// per-model numbers that a budget out of range answers with a 400 rather than a
    /// clamp. So they are measured rather than written down, and a model nobody has
    /// measured gets no <c>thinkingConfig</c> at all.
    /// </remarks>
    private static JsonObject? Thinking(SettingValues values, AssistantChoices chosen)
    {
        // Qualified because this class also has a Survey, and the method would
        // otherwise win the name over the type that stores what it found.
        var measured = Assist.Survey.Read(values.Text(Assist.Survey.Key, string.Empty))
            .FirstOrDefault(m => string.Equals(m.Id, chosen.Model, StringComparison.OrdinalIgnoreCase));

        if (measured?.Least is not { } least || measured.Most is not { } most) return null;

        var tokens = chosen.Effort switch
        {
            AssistantEffort.Low => least,
            AssistantEffort.High => most,
            _ => -1,
        };

        return new JsonObject { ["thinkingBudget"] = tokens };
    }
}
