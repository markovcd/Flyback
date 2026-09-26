namespace Flyback.Plugins.Settings;

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