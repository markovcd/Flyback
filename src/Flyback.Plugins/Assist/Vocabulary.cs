using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Which tool writes what a module carries that is not a knob.
/// </summary>
/// <remarks>
/// One fact in one place, on the side of the boundary that owns it. A tool name
/// is the assistant's vocabulary: it is declared in
/// <see cref="PatchWorkbench.Tools"/>, dispatched in
/// <see cref="PatchWorkbench.InvokeAsync"/>, and explained in
/// <see cref="Handbook"/> — three things in this project. It used to be a fourth
/// thing in <see cref="NodeExtra.Announce"/>, which is in the engine and cannot
/// see any of them: <c>Flyback.Plugins</c> references <c>Flyback.Core</c> and not
/// the other way round, so the engine was naming a tool it could not reference,
/// could not rename with, and could not check.
/// <para>
/// It had already gone wrong quietly, which is the argument for this being a
/// class rather than a tidy-up: the Handbook's preamble explained <c>notes</c>
/// and <c>scale</c> and never gained a line for <c>file</c> or <c>picture</c>,
/// because nothing connects the two. Renaming a tool would have gone wrong the
/// same way and worse — every module would have gone on telling the model to
/// call the old name, with nothing failing to say so.
/// </para>
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
