namespace Flyback.Plugins.Settings;

/// <summary>
/// One entry of an <see cref="SettingField.Pick"/>: the id that is stored, and
/// the name a person reads. Kept apart for the reason a patch keeps them apart —
/// what is written down has to go on meaning the same thing after somebody
/// rewords the label.
/// </summary>
/// <param name="Id">Stable, and what ends up in the settings file.</param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct SettingOption(string Id, string Name);

/// <summary>
/// One setting a plugin has, described rather than drawn.
/// </summary>
/// <remarks>
/// The plugin declares, the App draws (ADR-0055, ADR-0069, ADR-0085). A
/// credential is never a field, since it would land in the settings file in plain
/// text (ADR-0034); see <see cref="Assist.AssistantCredential"/>. Every value is
/// stored as a string, and every shape here is public API.
/// </remarks>
/// <param name="Key">What this value is filed under in the settings file; never change it.</param>
/// <param name="Label">What the form writes beside it.</param>
public abstract record SettingField(string Key, string Label)
{
    /// <summary>One line under the control, or null where the label says it all.</summary>
    /// <remarks>
    /// Where a plugin explains itself. The App has nothing to say about a model
    /// or a device it is forbidden to know one name of, so a sentence about the
    /// chosen one can only come from here.
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
    /// somebody may have edited. It is also what an unconfigured plugin starts on,
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
        string Placeholder = "") : SettingField(Key, Label)
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
    /// options are a plugin's suggestions rather than its permissions — a
    /// model name at an endpoint nobody here has heard of is the ordinary case,
    /// and a plain drop-down would make it unreachable.
    /// </param>
    public sealed record Pick(
        string Key,
        string Label,
        IReadOnlyList<SettingOption> Options,
        string Fallback = "",
        bool Editable = false) : SettingField(Key, Label)
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
    public sealed record Switch(string Key, string Label, bool On = false) : SettingField(Key, Label)
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
/// What everything on a form is set to, as the file holds it.
/// </summary>
/// <remarks>
/// A bag of strings rather than a record with properties, because the App cannot
/// name a single one of these: which settings exist is the plugin's to say. Value
/// equality, and it is load-bearing — the assistant panel compares the
/// configuration a conversation was started with against the one in front of
/// somebody now, and a reference comparison would start a new conversation on
/// every keystroke.
/// </remarks>
public sealed class SettingValues : IEquatable<SettingValues>
{
    private readonly Dictionary<string, string> held;

    public SettingValues(IEnumerable<KeyValuePair<string, string>>? from = null) =>
        held = from is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(from, StringComparer.Ordinal);

    /// <summary>Nothing set, which is what a plugin nobody has configured starts on.</summary>
    public static SettingValues None { get; } = new();

    /// <summary>Everything set, for whoever has to write it down.</summary>
    public IReadOnlyDictionary<string, string> All => held;

    /// <summary>This one set to <paramref name="value"/>, leaving the rest alone.</summary>
    public SettingValues With(string key, string value)
    {
        var next = new Dictionary<string, string>(held, StringComparer.Ordinal) { [key] = value };

        return new SettingValues(next);
    }

    /// <summary>What is stored, or <paramref name="fallback"/> where nothing meaningful is.</summary>
    public string Text(string key, string fallback = "") =>
        held.TryGetValue(key, out var stored) && !string.IsNullOrWhiteSpace(stored) ? stored : fallback;

    public bool Flag(string key, bool fallback = false) =>
        SettingField.Switch.Read(held.GetValueOrDefault(key), fallback);

    /// <summary>A stored value read back as one of an enum's names, however it was cased.</summary>
    public TWord Word<TWord>(string key, TWord fallback)
        where TWord : struct, Enum =>
        Enum.TryParse<TWord>(held.GetValueOrDefault(key), ignoreCase: true, out var word) ? word : fallback;

    public bool Equals(SettingValues? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (held.Count != other.held.Count) return false;

        foreach (var (key, value) in held)
            if (!other.held.TryGetValue(key, out var theirs) || !string.Equals(value, theirs, StringComparison.Ordinal))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as SettingValues);

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

