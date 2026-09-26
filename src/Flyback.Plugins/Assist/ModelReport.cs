using System.Text.Json.Serialization;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One model as an endpoint answered for it, rather than as somebody wrote it down.
/// </summary>
/// <remarks>
/// The same facts <see cref="AssistantModel"/> carries, plus the two a suggestion
/// cannot hold because they are arithmetic rather than a claim: what the model will
/// think for. A list of these is what <see cref="Survey"/> keeps in the settings so
/// the endpoint need not be asked again.
/// </remarks>
/// <param name="Id">What goes in the request.</param>
public sealed record ModelReport(string Id)
{
    /// <summary>Whether a picture was accepted.</summary>
    public bool Vision { get; init; } = true;

    /// <summary>Whether a sound was accepted.</summary>
    public bool Hearing { get; init; }

    /// <summary>The smallest thinking budget taken, where anybody measured.</summary>
    public int? Least { get; init; }

    /// <summary>The largest, where anybody measured.</summary>
    public int? Most { get; init; }

    /// <summary>What a form can be built from, which is the report minus the arithmetic.</summary>
    /// <remarks>
    /// Kept out of the file it is written to. It is derived from three fields
    /// that are already there, and a settings file carrying both says the same
    /// thing twice in a place somebody has to read by hand.
    /// </remarks>
    [JsonIgnore]
    public AssistantModel Suggestion => new(Id, Vision, Hearing);
}