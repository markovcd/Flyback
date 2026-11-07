using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Engine.Render;
using Flyback.Host;

namespace Flyback.Editor.Settings;

/// <summary>
/// The output settings a page keeps in the browser, which has no settings file: which edge
/// the transport waits at over a full-screen picture, and the oversampling the sound starts at.
/// </summary>
internal sealed class PageOutput
{
    /// <summary>The section a browser keeps it under.</summary>
    public const string Section = "output";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public TransportEdge Transport { get; set; }

    public int Oversample { get; set; } = AudioRenderer.DefaultOversample;

    /// <summary>Never throws: what cannot be read, or is not one of the choices, is the default.</summary>
    public static PageOutput Parse(string? json)
    {
        PageOutput kept;

        try
        {
            kept = json is not null ? JsonSerializer.Deserialize<PageOutput>(json, Options) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }

        if (!Enum.IsDefined(kept.Transport)) kept.Transport = TransportEdge.Top;
        if (!AudioRenderer.Oversamples.Contains(kept.Oversample)) kept.Oversample = AudioRenderer.DefaultOversample;

        return kept;
    }

    public static PageOutput Of(OutputSettings settings) => new() { Transport = settings.Transport, Oversample = settings.Oversample };

    public void Into(OutputSettings settings)
    {
        settings.Transport = Transport;
        settings.Oversample = Oversample;
    }

    public string Json() => JsonSerializer.Serialize(this, Options);
}
