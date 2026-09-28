using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Assist;

/// <summary>A key, and the one origin it is sent to (<see cref="IAssistantTransport.Origin"/>).</summary>
internal sealed record BoundKey(string Secret, string Origin)
{
    /// <summary>How a secret store holds it: both halves, since a key kept without its origin would go wherever it was next pointed.</summary>
    public string Stored() => new JsonObject { ["origin"] = Origin, ["secret"] = Secret }.ToJsonString();

    /// <summary>
    /// What a secret store held, or null for nothing. A key alone, as a store held one before
    /// keys had an origin, is bound to <paramref name="origin"/>, the one it is about to be sent to.
    /// </summary>
    public static BoundKey? Read(string? stored, string? origin)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            if (JsonNode.Parse(stored) is JsonObject bound
                && bound["secret"]?.GetValue<string>() is { } secret
                && bound["origin"]?.GetValue<string>() is { } kept)
                return string.IsNullOrWhiteSpace(secret) ? null : new BoundKey(secret, kept);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not one of ours: a key alone.
        }

        return origin is null ? null : new BoundKey(stored, origin);
    }

    /// <summary>Never the secret.</summary>
    public override string ToString() => $"a key for {Origin}";
}
