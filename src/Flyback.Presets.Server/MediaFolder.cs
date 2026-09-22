using System.Text.Json;

namespace Flyback.Presets.Server;

/// <summary>What the render app has made of a preset so far.</summary>
internal sealed record Media(string? Still, string? Loop, string? Audio, IReadOnlyList<double>? Peaks, string State);

/// <summary>
/// The folder the render app writes into and this server only reads.
/// </summary>
/// <remarks>
/// A preset's files are named by its id: <c>{id}.webp</c>, <c>{id}.webm</c>,
/// <c>{id}.mp3</c> and <c>{id}.peaks.json</c>. <c>{id}.done</c> marks it rendered
/// and <c>{id}.failed</c> marks a render that could not be done, so a preset is
/// pending while it has neither.
/// </remarks>
internal sealed class MediaFolder(string root)
{
    public const string Route = "/media";

    public string Root { get; } = root;

    public bool Pending(string id) =>
        !File.Exists(Path.Combine(Root, id + ".done")) && !File.Exists(Path.Combine(Root, id + ".failed"));

    public Media Of(string id)
    {
        string? Url(string suffix) =>
            File.Exists(Path.Combine(Root, id + suffix)) ? $"{Route}/{id}{suffix}" : null;

        var state = File.Exists(Path.Combine(Root, id + ".failed")) ? "failed"
            : File.Exists(Path.Combine(Root, id + ".done")) ? "done"
            : "pending";

        return new Media(Url(".webp"), Url(".webm"), Url(".mp3"), Peaks(id), state);
    }

    private double[]? Peaks(string id)
    {
        var path = Path.Combine(Root, id + ".peaks.json");

        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<double[]>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }
}
