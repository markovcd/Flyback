using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Graph;

namespace Flyback.App.Midi;

/// <summary>
/// The profiles Flyback knows: the ones it ships, and the ones in the user's
/// instruments folder, which win on a shared name.
/// </summary>
internal sealed class InstrumentLibrary
{
    private static readonly JsonSerializerOptions Reading = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private InstrumentLibrary(IReadOnlyList<InstrumentProfile> profiles) => Profiles = profiles;

    public IReadOnlyList<InstrumentProfile> Profiles { get; }

    /// <summary>Where a person puts a profile of their own, one <c>.json</c> per instrument.</summary>
    public static string UserFolder => Path.Combine(GlobalConstants.DataFolder, "instruments");

    /// <summary>Every profile shipped inside Flyback.</summary>
    public static InstrumentLibrary Shipped() => new(ReadShipped().ToList());

    /// <summary>The shipped profiles and those in <paramref name="userFolder"/>.</summary>
    /// <remarks>
    /// A file that will not read is skipped rather than fatal, since a profile
    /// somebody is halfway through writing is not a reason for the window not to
    /// open. What it said is lost, which is the price of not being asked.
    /// </remarks>
    public static InstrumentLibrary Load(string? userFolder = null)
    {
        var profiles = ReadShipped().ToList();

        foreach (var profile in ReadFolder(userFolder ?? UserFolder))
        {
            profiles.RemoveAll(existing => string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            profiles.Add(profile);
        }

        return new InstrumentLibrary(profiles);
    }

    /// <summary>A library of exactly these, for a test.</summary>
    public static InstrumentLibrary Of(params InstrumentProfile[] profiles) => new(profiles);

    /// <summary>The profile a port is an instance of, or null for one Flyback has never heard of.</summary>
    public InstrumentProfile? For(string id, string name) => Profiles.FirstOrDefault(profile => profile.Is(id, name));

    public InstrumentProfile? For(MidiSource source) => For(source.Id, source.Name);

    /// <summary>
    /// What a binding is called: by its instrument's names where the device is a
    /// known instrument, and the plain <see cref="MidiBinding.Label"/> otherwise.
    /// </summary>
    public string Describe(MidiBinding binding, MidiSource? device)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return device is { } source && For(source) is { } profile ? profile.Describe(binding) : binding.Label;
    }

    /// <summary>What fits under a knob: the short form where the device is known, the plain label otherwise.</summary>
    public string Label(MidiBinding binding, MidiSource? device)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return device is { } source && For(source) is { } profile ? profile.Label(binding) : binding.Label;
    }

    /// <summary>Reads one profile file, or null where it is not one.</summary>
    public static InstrumentProfile? Read(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<ProfileFile>(json, Reading);

            if (file is null || string.IsNullOrWhiteSpace(file.Name)) return null;

            return new InstrumentProfile(
                file.Name.Trim(),
                [.. (file.Matches ?? []).Where(match => !string.IsNullOrWhiteSpace(match)).Select(match => match.Trim())],
                file.Conducts,
                [.. (file.Tracks ?? [])
                    .Where(track => !string.IsNullOrWhiteSpace(track.Name) && track.Channel is >= 1 and <= 16)
                    .Select(track => new InstrumentTrack(
                        track.Name!.Trim(),
                        track.Channel,
                        track.Kind?.Trim(),
                        string.IsNullOrWhiteSpace(track.Short) ? null : track.Short.Trim()))],
                [.. (file.Pages ?? [])
                    .Where(page => !string.IsNullOrWhiteSpace(page.Name))
                    .Select(page => new InstrumentPage(
                        page.Name!.Trim(),
                        page.Kind?.Trim(),
                        [.. (page.Controls ?? [])
                            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is >= 0 and <= 127)
                            .Select(pair => new InstrumentControl(pair.Key.Trim(), pair.Value))]))]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<InstrumentProfile> ReadShipped()
    {
        var assembly = typeof(InstrumentLibrary).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith("instruments/", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);

            if (Read(reader.ReadToEnd()) is { } profile) yield return profile;
        }
    }

    private static IEnumerable<InstrumentProfile> ReadFolder(string folder)
    {
        if (!Directory.Exists(folder)) yield break;

        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            string json;

            try
            {
                json = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (Read(json) is { } profile) yield return profile;
        }
    }

    /// <summary>The file's shape, as written; <see cref="Read"/> tidies it.</summary>
    private sealed class ProfileFile
    {
        public string? Name { get; set; }

        public List<string>? Matches { get; set; }

        public bool Conducts { get; set; }

        public List<TrackFile>? Tracks { get; set; }

        public List<PageFile>? Pages { get; set; }
    }

    private sealed class TrackFile
    {
        public string? Name { get; set; }

        public int Channel { get; set; }

        public string? Kind { get; set; }

        public string? Short { get; set; }
    }

    private sealed class PageFile
    {
        public string? Name { get; set; }

        public string? Kind { get; set; }

        /// <summary>Knob name to controller number, in the instrument's order.</summary>
        public Dictionary<string, int>? Controls { get; set; }
    }
}