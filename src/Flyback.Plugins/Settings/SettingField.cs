namespace Flyback.Plugins.Settings;

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