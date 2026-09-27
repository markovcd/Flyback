using Flyback.Core.Graph;

namespace Flyback.App.Midi;

/// <summary>
/// What Flyback knows about one make of instrument: what its port is called,
/// which channel each of its tracks is on, which controller each of its knobs
/// sends, and whether it conducts.
/// </summary>
/// <remarks>
/// A file, not code, so a Digitakt or a Launchkey is a file somebody writes
/// rather than a feature somebody builds. Nothing in the engine reads one: a
/// patch still stores a channel number and a controller number, and the profile
/// only says what to call them and offers them from a list.
/// </remarks>
/// <param name="Name">What the instrument is called wherever a binding is described.</param>
/// <param name="Matches">Substrings of the port's name or id, any of which means this instrument.</param>
/// <param name="Conducts">Whether it sends MIDI clock, which makes it the one a fresh Clock In follows.</param>
/// <param name="Tracks">Its tracks, each on a channel of its own.</param>
/// <param name="Pages">Its knobs, grouped as the instrument groups them.</param>
internal sealed record InstrumentProfile(
    string Name,
    IReadOnlyList<string> Matches,
    bool Conducts,
    IReadOnlyList<InstrumentTrack> Tracks,
    IReadOnlyList<InstrumentPage> Pages)
{
    /// <summary>Whether a port with this id and name is this instrument.</summary>
    public bool Is(string id, string name) =>
        Matches.Any(match =>
            name.Contains(match, StringComparison.OrdinalIgnoreCase)
            || id.Contains(match, StringComparison.OrdinalIgnoreCase));

    /// <summary>The track on <paramref name="channel"/>, or null where none is.</summary>
    public InstrumentTrack? Track(int channel) => Tracks.FirstOrDefault(track => track.Channel == channel);

    /// <summary>The pages a track's knobs are on: those of its kind, and an ordinary track's are the ordinary pages.</summary>
    public IEnumerable<InstrumentPage> PagesOf(InstrumentTrack track) =>
        Pages.Where(page => string.Equals(page.Kind, track.Kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What a binding to this instrument is called: the track and the knob by
    /// name, e.g. "Syntakt · Track 3 · Filter Frequency", and as much of that as
    /// the binding has a name for.
    /// </summary>
    public string Describe(MidiBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var track = Track(binding.Channel);
        var where = track?.Name ?? (binding.Channel == 0 ? null : $"channel {binding.Channel}");
        var what = Knob(track, binding.Controller);

        return where is null ? $"{Name} · {what}" : $"{Name} · {where} · {what}";
    }

    /// <summary>
    /// The same, cut to fit under a knob: the track's short name and the knob,
    /// "T3 · Filter Frequency", with the instrument left off because every knob
    /// on a panel is usually on the one box.
    /// </summary>
    public string Label(MidiBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var track = Track(binding.Channel);
        var where = track?.Short ?? track?.Name ?? (binding.Channel == 0 ? null : $"ch {binding.Channel}");
        var what = Knob(track, binding.Controller);

        return where is null ? what : $"{where} · {what}";
    }

    private string Knob(InstrumentTrack? track, int controller) =>
        (track is null
            ? null
            : PagesOf(track)
                .SelectMany(page => page.Controls.Select(control => (Page: page, Control: control)))
                .Where(pair => pair.Control.Controller == controller)
                .Select(pair => $"{pair.Page.Name} {pair.Control.Name}")
                .FirstOrDefault())
        ?? $"CC{controller}";
}