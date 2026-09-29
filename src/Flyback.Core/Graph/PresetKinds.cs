namespace Flyback.Core.Graph;

/// <summary>What each <see cref="PresetKind"/> is called in a list of presets.</summary>
public static class PresetKinds
{
    /// <summary>
    /// The heading over a kind's run of presets, in the words somebody choosing a
    /// patch would use, since nobody opening the list is looking for an Interplay.
    /// </summary>
    public static string Heading(PresetKind kind) => kind switch
    {
        PresetKind.Idea => "ONE IDEA",
        PresetKind.Interplay => "SOUND AND PICTURE",
        PresetKind.Showcase => "SHOWCASE",
        _ => "BLANK",
    };
}
