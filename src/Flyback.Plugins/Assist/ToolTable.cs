using System.Text.Json;
using static Flyback.Plugins.Assist.ToolArguments;
using static Flyback.Plugins.Assist.ToolFields;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Every tool the workbench knows, each with what a provider is told of it and the
/// handler that runs it.
/// </summary>
internal sealed class ToolTable(
    PatchReports reports,
    WholePatchEdits whole,
    PatchProposals proposals,
    PatchSenses senses,
    PatchMeasurements measurements,
    CatalogReference catalog,
    WorkbenchLimits limits)
{
    /// <summary>
    /// What a tool does when it is called. Asynchronous for every tool because two of
    /// them are, and a dispatcher that had to know which is which would be back to
    /// knowing what each tool is.
    /// </summary>
    public delegate Task<ToolOutcome> ToolBody(JsonElement arguments, CancellationToken cancel);

    /// <summary>
    /// One tool, whole: what a provider is told about it, what it does, and whether
    /// it is offered at all.
    /// </summary>
    /// <remarks>
    /// The handler sits beside the schema because the two are one decision — the
    /// schema says what the arguments are and the handler reads them, through the
    /// same <see cref="ToolFields"/> (ADR-0008).
    /// <paramref name="Offered"/> is a flag rather than a fence around the entry, so
    /// a tool withheld from the model is still described in one place: dispatch
    /// knows every tool regardless, and only what a model may be offered depends on
    /// the model.
    /// </remarks>
    public sealed record Tool(PatchTool Spec, ToolBody Run, bool Offered);

    /// <summary>A tool that answers out of the graph it already has, which is most of them.</summary>
    private static Tool Does(
        string name,
        Func<JsonElement, ToolOutcome> run,
        string description,
        IReadOnlyList<ToolField> fields,
        bool offered = true) =>
        new(
            new PatchTool(name, description, ToolField.Schema(fields)),
            (arguments, _) => Task.FromResult(run(arguments)),
            offered);

    /// <summary>A tool that has to render something before it can answer.</summary>
    private static Tool Does(
        string name,
        ToolBody run,
        string description,
        IReadOnlyList<ToolField> fields,
        bool offered = true) =>
        new(new PatchTool(name, description, ToolField.Schema(fields)), run, offered);

    public IReadOnlyList<Tool> Build(bool vision, Listener hearing, bool lookups, bool readsPresets)
    {
        List<Tool> tools =
        [
            Does("describe_patch", _ => ToolOutcome.Fine(reports.Describe()),
                "Every module in the working patch with its handle, what each input is set to or "
                + "wired from, where each output goes, and what the compiler currently says. The "
                + "first message of a conversation already opens with it.",
                []),

            Does("write_patch", whole.WritePatch,
                "Builds a whole patch at once, written in the Flyback language, replacing whatever "
                + "is on the bench. Use it for a new patch. To change one that exists, use edit_patch, "
                + "which keeps the person's own text, comments and all, and costs a few lines rather "
                + "than the whole patch again. A module written under a let name the patch already "
                + "had stays that module.",
                [Source]),

            Does("edit_patch", whole.EditPatch,
                "Changes the patch by replacing pieces of its text, the text describe_patch shows: "
                + "a knob is a number to rewrite, a wire a name to pipe or pass, a module a line to "
                + "add or delete, a tune, a scale or a file whatever its block or string says. Each "
                + "'find' must be in the text exactly once, spaces and line breaks included; take in "
                + "enough around it to say which. Nothing changes unless every piece is found and the "
                + "edited patch reads. The answer shows the lines as they now read.",
                [Edits]),

            Does("reset", _ => whole.Reset(),
                "Throws away every edit and goes back to the patch as it was when this started.",
                []),

            Does("propose", proposals.ProposeAsync,
                "Offers the patch to the person, with one line saying what it does. This ends your "
                + "turn. Nothing you have built reaches their editor until you call this. The patch "
                + "must compile cleanly first, something must reach the Output — its 'color', "
                + "its 'left', or both — and a sound that is wired must not be silent.",
                [
                    Summary,
                    StartsSilent.Described(
                        $"True only when the sound is meant to be silent for its first {Number(limits.LatestTime)}s, an intro that comes in later."),
                ]),

            Does("render", senses.RenderAsync,
                "Draws the patch and shows you the result: several frames side by side, so you can "
                + "see movement as well as color. Use it once the shape is right, and again after "
                + "adjusting what you saw.",
                [
                    From.Described(
                        $"Where on the patch's timeline the frames start, up to {Number(limits.LatestStart)}. Defaults to 0. "
                        + $"Drawing begins {Number(limits.WarmUpLead)}s before it, so anything that remembers earlier frames has only that much history."),
                    Times.Described(
                        $"Seconds after 'from' to capture, at most {limits.MaxFrames} of them, up to {Number(limits.LatestTime)}. "
                        + "Defaults to 0.5, 1.5 and 3.5."),
                    Note.Described("What you are looking for, for your own record."),
                ],
                offered: vision),

            Does("listen", senses.ListenAsync,
                "Renders the patch's sound, measures its level, loudness and spectrum, and "
                + (hearing is Listener.Itself
                    ? "plays it to you — the clip arrives after this reply, the way a rendered "
                      + "frame does. "
                    : "has a model that can hear describe it to you. ")
                + "Use it once the sound is wired, and again after adjusting what you found. It is "
                + "the only way to find out whether a patch built for the speakers is anything at "
                + "all, as opposed to merely legal.",
                [
                    Seconds.Described($"How much to render, from 0.25 to {Number(limits.LongestListen)}. Defaults to 2."),
                    From.Described(
                        $"Where on the timeline to start, up to {Number(limits.LatestStart)}. Defaults to 0. Sound begins "
                        + $"{Number(limits.ListenLead)}s before it, so delays arrive with a tail but anything longer than that is missing."),
                    Note.Described($"What you are listening for, for your own record. {Kept(hearing)}"),
                ],
                offered: hearing is not Listener.None),

            Does("measure", measurements.MeasureAsync,
                "Runs the patch for a few seconds with nothing played in and says what each output "
                + "carries, as numbers: its value when it holds still, otherwise its range, mean and "
                + "how fast it moves (hertz, steps a second, or steepest slope), and whether it "
                + "varies across the picture. Every output of the modules named, wired to anything "
                + "or not, or of every module when none are named. The sound and the picture are "
                + "measured apart, since memory and coordinates make them differ. Reach for it to "
                + "check what a knob or an LFO really gives before wiring it, or why something is "
                + "flat; it costs no picture and no clip.",
                [
                    Handles,
                    Seconds.Described(
                        $"How long to run, from 0.25 to {Number(PatchMeasurements.LongestMeasure)}. Defaults to 2. "
                        + "A repeat needs a few cycles in the window to be counted."),
                    From.Described(
                        $"Where on the patch's timeline the window starts, up to {Number(limits.LatestStart)}. Defaults to 0. "
                        + "Anything that remembers earlier moments starts empty there."),
                ]),

            Does("describe_module", catalog.DescribeModule,
                "Everything about one module: what it is for, and each port's default, range and "
                + "what it is for.",
                [TypeId]),

            Does("find_modules", catalog.FindModules,
                "Searches the module list by type id, name, category or description.",
                [Query],
                offered: lookups),

            Does("describe_preset", catalog.DescribePreset,
                "Reads one of the presets in the list at the end of the briefing, written in the "
                + "Flyback language, to see how it is built. It does not touch the patch on the bench.",
                [PresetName],
                offered: readsPresets),
        ];

        return tools;
    }

    /// <summary>
    /// What becomes of <c>listen</c>'s <c>note</c>, which depends on whether there
    /// is anybody else to keep it from.
    /// </summary>
    /// <remarks>
    /// Withholding it is the second-hand arrangement's one safeguard — a listener
    /// told what to listen for will find it, and ADR-0047 records the clip where it
    /// did. There is nobody to withhold it from when the model plays itself the
    /// clip.
    /// </remarks>
    private static string Kept(Listener hearing) => hearing is Listener.Itself
        ? "Nobody else reads it."
        : "It is deliberately not passed on: whoever listens is told nothing about the patch, "
          + "so that what comes back could disagree with you.";
}
