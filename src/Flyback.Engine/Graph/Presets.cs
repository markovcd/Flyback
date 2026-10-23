using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Engine.Graph;

/// <summary>Patches that ship with the synth, so it never opens on a blank canvas.</summary>
internal static class Presets
{
    /// <summary>
    /// Everything the engine ships, in the order the picker shows it: the blank
    /// canvas, then ideas, then interplay, then the big ones.
    /// </summary>
    public static IReadOnlyList<PatchPreset> All => [.. Shipped.Select(Fused)];

    /// <summary>
    /// A preset whose chains of Maths modules arrive folded into Expressions —
    /// see <see cref="ExpressionFusion"/> — and laid out again with what is left.
    /// </summary>
    public static PatchPreset Fused(PatchPreset preset) => preset with
    {
        Build = modules =>
        {
            var patch = ExpressionFusion.Fuse(preset.Build(modules), modules);

            PatchLayout.Arrange(patch, modules);
            return patch;
        },
    };

    /// <summary>The presets as written, before <see cref="Fused"/>.</summary>
    internal static IReadOnlyList<PatchPreset> Shipped { get; } =
    [
        // --- nothing yet -------------------------------------------------------

        // Its text says nothing, so a patch started from it has no description of its own.
        Offered("Empty", Empty, PresetKind.Blank) with
        {
            Description = "The Output, with everything still to plug into it.",
            Tags = ["basics"],
        },
        Offered("Picture in", PictureIn, PresetKind.Blank),
        Offered("Clip", Clip, PresetKind.Blank),

        // --- one idea, one sink ------------------------------------------------

        Offered("Plasma", Plasma),
        Offered("Kaleidoscope", Kaleidoscope),
        Offered("Grid", Grid),
        Offered("Three channels", ThreeChannels),
        Offered("Feedback tunnel", FeedbackTunnel),
        Offered("Trails", Trails),
        Offered("Loop", Loop),
        Offered("Two channels", TwoChannels),
        Offered("Staircase", Staircase),
        Offered("Heads or tails", HeadsOrTails),
        Offered("Nebula", Nebula),

        // --- the two sinks meeting ---------------------------------------------

        Offered("Drone", Drone, PresetKind.Interplay),
        Offered("Sequence", Sequence, PresetKind.Interplay),
        Offered("Four voices", FourVoices, PresetKind.Interplay),
        Offered("Heard", Heard, PresetKind.Interplay),
        Offered("Duck", Duck, PresetKind.Interplay),
        Offered("Waveform", Waveform, PresetKind.Interplay),
        Offered("Sidebands", Sidebands, PresetKind.Interplay),
        Offered("Ahead and behind", AheadAndBehind, PresetKind.Interplay),
        Offered("Lissajous", Lissajous, PresetKind.Interplay),
        Offered("In key", InKey, PresetKind.Interplay),
        Offered("Ring scan", RingScan, PresetKind.Interplay),

        // --- what one patch can be ---------------------------------------------

        Offered("Whole band", WholeBand, PresetKind.Showcase),
    ];

    /// <summary>Just the Output, with everything still to plug into it.</summary>
    public static Patch Empty(ModuleCatalog modules) => Text("Empty.fbks", modules);

    /// <summary>
    /// A photograph read as a field, and put through the same geometry a
    /// generated one goes through.
    /// </summary>
    public static Patch PictureIn(ModuleCatalog modules) => Text("PictureIn.fbks", modules);

    /// <summary>
    /// The sample player, wired the three ways it is worth wiring: played
    /// forward on the clock, restarted by a trigger, and scrubbed by a signal.
    /// </summary>
    public static Patch Clip(ModuleCatalog modules) => Text("Clip.fbks", modules);

    /// <summary>Three drifting sine fields summed and read through a cosine palette — the "hello world" of video synths.</summary>
    public static Patch Plasma(ModuleCatalog modules) => Text("Plasma.fbks", modules);

    /// <summary>Rotating wedges filled with clouds that boil over time.</summary>
    public static Patch Kaleidoscope(ModuleCatalog modules) => Text("Kaleidoscope.fbks", modules);

    /// <summary>
    /// The three coordinate transforms nothing else in the box shows, in a row,
    /// so that what each does to the plane can be seen by taking it out.
    /// </summary>
    public static Patch Grid(ModuleCatalog modules) => Text("Grid.fbks", modules);

    /// <summary>
    /// One field read at three sizes, one reading to each of red, green and
    /// blue: a color is three signals, and nothing says they have to agree.
    /// </summary>
    public static Patch ThreeChannels(ModuleCatalog modules) => Text("ThreeChannels.fbks", modules);

    /// <summary>
    /// The camera-pointed-at-its-own-monitor patch: each frame is re-read
    /// slightly rotated, scaled and dimmed, with fresh rings fed in on top.
    /// </summary>
    public static Patch FeedbackTunnel(ModuleCatalog modules) => Text("FeedbackTunnel.fbks", modules);

    /// <summary>
    /// A dot steered round a looping path by two sines, and a Trails keeping
    /// where it has been.
    /// </summary>
    public static Patch Trails(ModuleCatalog modules) => Text("Trails.fbks", modules);

    /// <summary>
    /// One wire running backwards, which is a lowpass built by hand out of an add
    /// and a multiply, with the one number that is its cutoff swept so that it
    /// can be heard to be one.
    /// </summary>
    public static Patch Loop(ModuleCatalog modules) => Text("Loop.fbks", modules);

    /// <summary>
    /// Two channels that are two signals, rather than one signal made quieter on
    /// one side.
    /// </summary>
    public static Patch TwoChannels(ModuleCatalog modules) => Text("TwoChannels.fbks", modules);

    /// <summary>
    /// A Sample &amp; Hold catching a slow slope on every tick of a clock: what
    /// comes out is a staircase, and a staircase put through a scale is a tune.
    /// </summary>
    public static Patch Staircase(ModuleCatalog modules) => Text("Staircase.fbks", modules);

    /// <summary>
    /// One riff and a coin for every note: heads plays it on the left, tails an
    /// octave down on the right, and the odds drift from one side to the other.
    /// </summary>
    public static Patch HeadsOrTails(ModuleCatalog modules) => Text("HeadsOrTails.fbks", modules);

    /// <summary>
    /// Everything the video side can do, in one patch: coordinates turned, folded
    /// into wedges, bent by a cloud field read from inside the fold, taken as
    /// traveling rings, and laid over a trail of its own previous frames.
    /// </summary>
    public static Patch Nebula(ModuleCatalog modules) => Text("Nebula.fbks", modules);

    /// <summary>
    /// One patch heard and seen at once. A single slow oscillator sets both the
    /// hue of the image and the tremolo on the tone, so the two sinks are
    /// visibly and audibly the same signal. "The patch as text" reads it line by line.
    /// </summary>
    public static Patch Drone(ModuleCatalog modules) => Text("Drone.fbks", modules);

    /// <summary>
    /// One sequencer, heard and seen at once. The steps are the tune; where the
    /// sequence has got to is the color, and the gate that makes a rest silent
    /// is the same one that takes the light out of it.
    /// </summary>
    public static Patch Sequence(ModuleCatalog modules) => Text("Sequence.fbks", modules);

    /// <summary>
    /// Four voices, each with a fader, through one Mixer at each sink. The
    /// faders are the shared signal: level three on the chord is level three on
    /// the screen, so what fades up in the sound is the same thing that fades up
    /// in the picture.
    /// </summary>
    public static Patch FourVoices(ModuleCatalog modules) => Text("FourVoices.fbks", modules);

    /// <summary>
    /// A drum with the picture listening to it rather than being told about the
    /// beat.
    /// </summary>
    public static Patch Heard(ModuleCatalog modules) => Text("Heard.fbks", modules);

    /// <summary>
    /// A kick and a pad, and a Duck turning the pad down under every hit so the
    /// kick has the room to itself. The Scope draws the level the Duck applies,
    /// which falls with every hit and climbs back before the next.
    /// </summary>
    public static Patch Duck(ModuleCatalog modules) => Text("Duck.fbks", modules);

    /// <summary>
    /// The four wave shapes at one pitch, faded one into the next, and a Scope
    /// drawing what the speakers actually played — so a change of timbre is heard
    /// while the change of shape that makes it is on the screen.
    /// </summary>
    public static Patch Waveform(ModuleCatalog modules) => Text("Waveform.fbks", modules);

    /// <summary>
    /// Frequency modulation built by hand: one sine into another's phase, the
    /// amount struck and left to fall, and an Analyzer on what comes out.
    /// </summary>
    public static Patch Sidebands(ModuleCatalog modules) => Text("Sidebands.fbks", modules);

    /// <summary>
    /// A Probe and a Scope reading the same wire, one above the other, which is
    /// the only arrangement in which the difference between them is visible.
    /// </summary>
    public static Patch AheadAndBehind(ModuleCatalog modules) => Text("AheadAndBehind.fbks", modules);

    /// <summary>
    /// The patch the "Seeing a sound" video ends on: two sines, one to each
    /// speaker, drawn against each other by a Beam. A fifth is three cycles
    /// against two, which is the knot; the right one is a fifth of a hertz sharp,
    /// so the knot turns, back to the same shape every two and a half seconds.
    /// </summary>
    public static Patch Lissajous(ModuleCatalog modules) => Text("Lissajous.fbks", modules);

    /// <summary>
    /// A cloud field played as a melody and drawn as the terraces it is being
    /// snapped to: one Quantiser, in a pentatonic, feeding both sinks.
    /// </summary>
    public static Patch InKey(ModuleCatalog modules) => Text("InKey.fbks", modules);

    /// <summary>
    /// The picture played rather than drawn. A circle is swept round a field of
    /// rings at audio rate and what it passes over is the waveform, so the tone
    /// is not made by an oscillator anywhere — it is the image, read along a
    /// line.
    /// </summary>
    public static Patch RingScan(ModuleCatalog modules) => Text("RingScan.fbks", modules);

    /// <summary>
    /// A whole song out of nothing but the engine's own modules: a kick, a snare
    /// and hats, a bass through a filter, two plucked strings, a pad and a lead,
    /// in a room — arranged into twelve phrases of eight bars, with a picture every
    /// one of the parts moves.
    /// </summary>
    public static Patch WholeBand(ModuleCatalog modules) => Text("WholeBand.fbks", modules);

    /// <summary>
    /// <paramref name="build"/>, offered as <paramref name="name"/> with the description
    /// and tags of the text it builds, which is named for it.
    /// </summary>
    private static PatchPreset Offered(string name, Func<ModuleCatalog, Patch> build, PresetKind kind = PresetKind.Idea)
    {
        var header = SourceHeader.Read(Source(build.Method.Name + ".fbks"));

        return new PatchPreset(name, build, header.Description, kind) { Tags = header.Tags };
    }

    /// <summary>Builds the embedded <c>presets/</c> text <paramref name="name"/> against <paramref name="modules"/>.</summary>
    /// <remarks>A shipped text that does not build throws with every complaint, since that is a bug here.</remarks>
    private static Patch Text(string name, ModuleCatalog modules)
    {
        var load = PatchLanguage.Build(Source(name), modules);

        return load.Ok ? load.Patch : throw new InvalidOperationException($"{name} does not build:{Environment.NewLine}{load.Report}");
    }

    private static string Source(string name)
    {
        using var stream = typeof(Presets).Assembly.GetManifestResourceStream("presets/" + name)
            ?? throw new InvalidOperationException($"No preset text '{name}' is embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
