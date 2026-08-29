using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Which tool writes what a module carries that is not a knob.
/// </summary>
/// <remarks>
/// The tool name is declared once, dispatched by the workbench, and described in
/// the handbook.
/// </remarks>
internal static class Vocabulary
{
    public const string SetSteps = "set_steps";
    public const string SetScale = "set_scale";
    public const string SetSample = "set_sample";
    public const string SetPicture = "set_picture";
    public const string SetExtra = "set_extra";

    /// <summary>
    /// The tool that writes the extra filed under <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// Anything not named here is <see cref="SetExtra"/>, which is right by
    /// construction rather than by luck: the four below are the kinds the engine
    /// ships and the only ones with a tool written for them, and every other kind
    /// is one a plugin declared through <see cref="NodeExtra.Fields"/> — which is
    /// exactly what <c>set_extra</c> exists to write
    /// ([0055](0055-a-plugins-extra-declares-its-editor.md)).
    /// </remarks>
    public static string ToolFor(string key) => key switch
    {
        StepsExtra.Name => SetSteps,
        ScaleExtra.Name => SetScale,
        SampleExtra.Name => SetSample,
        PictureExtra.Name => SetPicture,
        _ => SetExtra,
    };

    /// <summary>
    /// One extra's line in a module listing: what the module carries, which the
    /// engine says, and how to write it, which this project says.
    /// </summary>
    public static string Announce(NodeExtra extra) =>
        $"{extra.Announce()}, set with {ToolFor(extra.Key)}";
}
