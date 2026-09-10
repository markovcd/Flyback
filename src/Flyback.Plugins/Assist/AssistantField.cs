namespace Flyback.Plugins.Assist;

/// <summary>
/// One entry of an <see cref="AssistantField.Pick"/>: the id that is stored, and
/// the name a person reads. Kept apart for the reason a patch keeps them apart —
/// what is written down has to go on meaning the same thing after somebody
/// rewords the label.
/// </summary>
/// <param name="Id">Stable, and what ends up in the settings file.</param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct AssistantOption(string Id, string Name);

/// <summary>
/// One setting a provider has, described rather than drawn.
/// </summary>
/// <remarks>
/// The declarative route ADR-0055 took for a plugin's carried state, taken again
/// for a provider's settings: the plugin says what it has and the App draws it, so
/// no plugin ships a control.
/// <para>
/// <b>A credential is never a field</b> (ADR-0034): a plugin that declared
/// somewhere to type one would be a plugin whose settings file held it in plain
/// text. What a provider says about its key is
/// <see cref="AssistantCredential"/>.
/// </para>
/// <para>
/// A value is a string whatever the shape — a switch is one or nought, a choice is
/// the id it chose — which keeps the settings file readable by hand. The
/// vocabulary is short on purpose, and every shape in it is public API that cannot
/// be withdrawn.
/// </para>
/// </remarks>
/// <param name="Key">
/// What this value is filed under. Stable: it is in the settings file of everybody
/// who has ever configured this provider.
/// </param>
/// <param name="Label">What the form writes beside it.</param>
public abstract record AssistantField(string Key, string Label)
{
    /// <summary>One line under the control, or null where the label says it all.</summary>
    /// <remarks>
    /// Where a provider explains itself. The App has nothing to say about a
    /// model it is forbidden to know one name of, so a sentence about what the
    /// chosen one accepts can only come from here.
    /// </remarks>
    public string? Note { get; init; }

    /// <summary>
    /// Whether this may be changed as things stand. False leaves it on the form,
    /// showing what it holds.
    /// </summary>
    /// <remarks>
    /// Off rather than absent is for a question that is still a question and cannot
    /// be answered yet — a fixed endpoint, an ear nobody has asked to listen. A
    /// setting that has stopped meaning anything is left out of the form instead,
    /// since a disabled control asks somebody to work out why it is there.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>Why it cannot be changed, shown on hover. Ignored while <see cref="Enabled"/>.</summary>
    public string? Because { get; init; }

    /// <summary>
    /// The stored value held to what this field can mean, and the field's own
    /// default where it means nothing at all.
    /// </summary>
    /// <remarks>
    /// Every read goes through here rather than trusting the file: settings are text
    /// somebody may have edited. It is also what an unconfigured provider starts on,
    /// since "nothing set yet" is the same question.
    /// </remarks>
    public abstract string Sane(string? stored);

    /// <summary>Free text — an endpoint, a deployment name, anything nobody can list.</summary>
    /// <param name="Fallback">What it holds until somebody types something.</param>
    /// <param name="Placeholder">Shown in the empty box.</param>
    public sealed record Text(
        string Key,
        string Label,
        string Fallback = "",
        string Placeholder = "") : AssistantField(Key, Label)
    {
        public override string Sane(string? stored) =>
            string.IsNullOrWhiteSpace(stored) ? Fallback : stored;
    }

    /// <summary>
    /// One of a list of named things — a model, an ear, a level of effort.
    /// </summary>
    /// <param name="Options">
    /// What there is to choose from. May be empty, which is a real answer for a
    /// list that depends on what else is set.
    /// </param>
    /// <param name="Fallback">What is chosen until somebody chooses.</param>
    /// <param name="Editable">
    /// Whether anything at all may be typed over the list. True where the
    /// options are a provider's suggestions rather than its permissions — a
    /// model name at an endpoint nobody here has heard of is the ordinary case,
    /// and a plain drop-down would make it unreachable.
    /// </param>
    public sealed record Pick(
        string Key,
        string Label,
        IReadOnlyList<AssistantOption> Options,
        string Fallback = "",
        bool Editable = false) : AssistantField(Key, Label)
    {
        /// <summary>
        /// Whatever was stored, which is deliberately not held to
        /// <see cref="Options"/>: an id that is not in the list is a model released
        /// after this build, or one at an endpoint somebody pointed at by hand.
        /// Falling back would quietly reconfigure their provider.
        /// </summary>
        public override string Sane(string? stored) =>
            string.IsNullOrWhiteSpace(stored) ? Fallback : stored;

        /// <summary>
        /// What to call <paramref name="id"/>, and the id itself where nothing in
        /// the list answers to it.
        /// </summary>
        public string Name(string id)
        {
            foreach (var option in Options)
                if (option.Id == id)
                    return option.Name;

            return id;
        }
    }

    /// <summary>Something that is either on or off.</summary>
    /// <param name="On">What it holds until somebody sets it.</param>
    public sealed record Switch(string Key, string Label, bool On = false) : AssistantField(Key, Label)
    {
        /// <summary>How a switch is spelled in the file, and the only two things it can say.</summary>
        private const string Yes = "1";

        private const string No = "0";

        public override string Sane(string? stored) => Spell(Read(stored, On));

        /// <summary>This field's value as the switch it is.</summary>
        public bool Value(string? stored) => Read(stored, On);

        /// <summary>A switch written the one way it is written, for whoever has to store one.</summary>
        public static string Spell(bool on) => on ? Yes : No;

        /// <summary>
        /// True and false are read as well as one and nought, because a settings
        /// file is hand-editable and that is what a person writes.
        /// </summary>
        internal static bool Read(string? stored, bool fallback) => stored?.Trim() switch
        {
            Yes or "true" or "True" => true,
            No or "false" or "False" => false,
            _ => fallback,
        };
    }
}

/// <summary>
/// What a provider says about the key it needs, which the host holds and the
/// plugin never sees a place to store.
/// </summary>
/// <remarks>
/// Apart from <see cref="AssistantField"/> because it is the one part of a
/// provider's configuration that must not become one (ADR-0034): the host reads
/// the variable and draws the box, and the key reaches a plugin only as
/// <see cref="AssistantConfig.ApiKey"/>, for the length of a run.
/// </remarks>
/// <param name="EnvironmentVariable">
/// The variable this provider is conventionally given its key in. The shell reads
/// it, not the plugin.
/// </param>
/// <param name="Help">One line saying where a key comes from, shown under the field.</param>
public sealed record AssistantCredential(string EnvironmentVariable, string Help);

/// <summary>
/// What everything on a form is set to, as the file holds it.
/// </summary>
/// <remarks>
/// A bag of strings rather than a record with properties, because the App cannot
/// name a single one of these: which settings exist is the provider's to say. Value
/// equality, and it is load-bearing — the panel compares the configuration a
/// conversation was started with against the one in front of somebody now, and a
/// reference comparison would start a new conversation on every keystroke.
/// </remarks>
public sealed class AssistantValues : IEquatable<AssistantValues>
{
    private readonly Dictionary<string, string> held;

    public AssistantValues(IEnumerable<KeyValuePair<string, string>>? from = null) =>
        held = from is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(from, StringComparer.Ordinal);

    /// <summary>Nothing set, which is what a provider nobody has configured starts on.</summary>
    public static AssistantValues None { get; } = new();

    /// <summary>Everything set, for whoever has to write it down.</summary>
    public IReadOnlyDictionary<string, string> All => held;

    /// <summary>This one set to <paramref name="value"/>, leaving the rest alone.</summary>
    public AssistantValues With(string key, string value)
    {
        var next = new Dictionary<string, string>(held, StringComparer.Ordinal) { [key] = value };

        return new AssistantValues(next);
    }

    /// <summary>What is stored, or <paramref name="fallback"/> where nothing meaningful is.</summary>
    public string Text(string key, string fallback = "") =>
        held.TryGetValue(key, out var stored) && !string.IsNullOrWhiteSpace(stored) ? stored : fallback;

    public bool Flag(string key, bool fallback = false) =>
        AssistantField.Switch.Read(held.GetValueOrDefault(key), fallback);

    /// <summary>A stored value read back as one of an enum's names, however it was cased.</summary>
    public TWord Word<TWord>(string key, TWord fallback)
        where TWord : struct, Enum =>
        Enum.TryParse<TWord>(held.GetValueOrDefault(key), ignoreCase: true, out var word) ? word : fallback;

    public bool Equals(AssistantValues? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (held.Count != other.held.Count) return false;

        foreach (var (key, value) in held)
            if (!other.held.TryGetValue(key, out var theirs) || !string.Equals(value, theirs, StringComparison.Ordinal))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as AssistantValues);

    public override int GetHashCode()
    {
        // Order-independent, because two bags holding the same pairs are the
        // same bag however they were built up.
        var hash = held.Count;

        foreach (var (key, value) in held)
            hash ^= HashCode.Combine(key, value);

        return hash;
    }
}

/// <summary>
/// What a run configured this way may be handed, which the host has to know
/// because the host builds the workbench.
/// </summary>
/// <remarks>
/// The one thing the App still asks a provider about its settings, and it asks in
/// terms of what happens rather than what was chosen: whether a frame may be shown,
/// and who is played the sound. Which model that is stays the provider's business.
/// </remarks>
/// <param name="Vision">Whether the model may be shown a rendered frame.</param>
/// <param name="Hearing">Who listens, and <see cref="Listener.None"/> for nobody.</param>
public readonly record struct AssistantSenses(bool Vision = true, Listener Hearing = Listener.None);
