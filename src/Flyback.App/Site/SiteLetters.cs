using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Flyback.App.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Site;

/// <summary>Writing to the author from inside Flyback, through the preset site.</summary>
/// <remarks>
/// What goes up is what the letter shows: nothing about the machine beyond its
/// operating system, no path, and no patch. The address is the person's own to
/// give or leave blank.
/// </remarks>
internal static class SiteLetters
{
    /// <summary>What a letter may be about, as the site takes them, with what each says.</summary>
    public static readonly IReadOnlyList<(string Mood, string Said)> Moods =
    [
        ("good", "Something works well"),
        ("bad", "Something is wrong"),
        ("idea", "Something I would like"),
        ("other", "Something else"),
    ];

    public const int MessageLimit = 2000;

    public const int ContactLimit = 200;

    /// <summary>What this copy says about itself.</summary>
    public static LetterAbout About(PluginCatalog plugins, AudioSetup sound) => new(
        Controls.About.Version,
        RuntimeInformation.OSDescription,
        PluginLine(plugins, sound));

    /// <summary>
    /// Posts the letter. Throws where the site cannot be reached or does not take it.
    /// </summary>
    public static async Task SendAsync(
        HttpClient http, Uri root, string mood, string message, string? contact, LetterAbout about, CancellationToken cancel)
    {
        var at = new Uri(root, "api/v1/letters");

        using var response = await http.PostAsJsonAsync(
            at,
            new { mood, message, contact, about.Version, about.Platform, about.Plugins },
            cancel);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The plugins that loaded and the backend playing, on one line.
    /// </summary>
    /// <remarks>
    /// Names rather than ids, and the folder they came from is left out: it holds
    /// the person's own account name, which is exactly the kind of thing a letter
    /// should not carry without being asked for.
    /// </remarks>
    private static string PluginLine(PluginCatalog plugins, AudioSetup sound)
    {
        var loaded = plugins.Plugins.Count == 0
            ? "no plugins"
            : string.Join(", ", plugins.Plugins.Select(p => p.Info.Name));

        var playing = sound.Failure is { } failure ? $"sound failed ({failure})"
            : sound.Output is { } output ? $"sound: {output.Name}"
            : "no sound";

        return $"{loaded}; {playing}";
    }
}
