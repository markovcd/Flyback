namespace Flyback.Plugins.Midi;

/// <summary>
/// How a backend turns the names it read off the machine into ids a patch can
/// keep.
/// </summary>
/// <remarks>
/// Here rather than in each backend because every backend has the same problem
/// and only one right answer to it. What comes off a driver is a display name —
/// "Launchkey Mini MK3", "USB MIDI Interface" — and what a patch needs is
/// something that is the same tomorrow, in another socket, after a reboot, and
/// is not the port number that changes when any of that happens.
/// </remarks>
public static class MidiPorts
{
    /// <summary>
    /// What every hardware id begins with.
    /// </summary>
    /// <remarks>
    /// Not decoration. <c>MidiSources.Keyboard</c> is the id <c>keyboard</c>, and
    /// a device that happens to be called "Keyboard" would otherwise take its
    /// place and swallow the one instrument that is always there.
    /// </remarks>
    public const string Prefix = "midi:";

    /// <summary>
    /// Ids for a backend's devices, in the order it enumerated them.
    /// </summary>
    /// <remarks>
    /// Two of the same model on one machine is the case that has no good answer:
    /// the driver gives both the same name, so the only thing left to tell them
    /// apart with is the order they were found in, and that is exactly what an
    /// id is not supposed to depend on. They are numbered, and the second one
    /// becomes the first if the pair is swapped over — which is wrong, and is
    /// less wrong than one of the two being unreachable.
    /// </remarks>
    public static IReadOnlyList<MidiPortInfo> Named(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var ports = new List<MidiPortInfo>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            var display = string.IsNullOrWhiteSpace(name) ? "MIDI device" : name.Trim();
            var id = Prefix + Slug(display);

            if (seen.TryGetValue(id, out var already))
            {
                seen[id] = already + 1;
                ports.Add(new MidiPortInfo($"{id}-{already + 1}", $"{display} ({already + 1})"));
                continue;
            }

            seen[id] = 1;
            ports.Add(new MidiPortInfo(id, display));
        }

        return ports;
    }

    /// <summary>
    /// A name reduced to what will still be legible in a patch file a year from
    /// now: lower case, ASCII letters and digits, and a hyphen wherever anything
    /// else was.
    /// </summary>
    /// <remarks>
    /// Runs of hyphens collapse and the ends are trimmed, so "Launchkey Mini
    /// [MK3]" and "Launchkey  Mini  MK3" are the same device rather than two.
    /// Non-ASCII goes to a hyphen rather than being transliterated: a device with
    /// a Japanese name becomes a row of hyphens and a number, which is ugly and
    /// is still stable, and the picker shows the real name regardless.
    /// </remarks>
    private static string Slug(string name)
    {
        var slug = new System.Text.StringBuilder(name.Length);

        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        while (slug.Length > 0 && slug[^1] == '-') slug.Length--;

        return slug.Length == 0 ? "device" : slug.ToString();
    }
}