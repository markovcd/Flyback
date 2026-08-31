using System.Globalization;
using System.Text;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The synth as a model should be told it: the conventions the catalogue cannot
/// state, and then every module in it.
/// </summary>
/// <remarks>
/// Byte-for-byte deterministic, which is load-bearing rather than tidy. This
/// text is the stable prefix of every request in a run, and a provider that
/// caches prefixes only keeps doing so while the bytes are identical — a sort
/// order that varies, or a timestamp anywhere in here, turns a cache read back
/// into a cache write on every single turn.
/// </remarks>
internal static class Handbook
{
    /// <summary>
    /// Above this many characters the module prose is dropped and the assistant
    /// is handed tools to look modules up instead. A cached prefix beats a round
    /// trip at every size that fits in one; this is roughly where it stops
    /// fitting.
    /// </summary>
    public const int ProseBudget = 40_000;

    /// <summary>
    /// What the catalogue cannot say about itself. Hand-written, and the place
    /// to state a convention that lives in an ADR rather than in a type.
    /// </summary>
    private const string Conventions = """
        # Flyback

        A patchable synthesiser for picture and sound. Nothing is drawn: every
        frame is a function evaluated once per pixel, and every sample is the
        same function evaluated once per tick. You build that function by
        placing modules and wiring them together.

        ## Coordinates and values

        - `y` runs -1 at the bottom to 1 at the top.
        - `x` is the same scale widened by the aspect ratio, so it runs about
          -1.78 to 1.78 on a 16:9 frame. That is what keeps circles circular:
          `Length(x, y)` is a true radius.
        - `t` is seconds since the patch started. It reaches a patch through
          the **Time** module, or through a socket normalled to it — see
          below.
        - What reaches the screen is 0..1 per channel, clamped, with no gamma. A
          value of 0.5 is mid grey; 4 and 1 are the same white; -1 is black.
          There is no headroom to pull back down later.

        ## Putting a patch together

        - Every input is a knob with a value on it. Most inputs in a real patch
          are left as knobs; you only wire the ones that need to move.
        - A wire into an input overrides its knob. The knob is not lost — it
          comes back if the wire is removed.
        - An input takes at most one wire. Wiring a second one replaces the
          first, and the tool will tell you when it did.
        - An output may fan out to as many inputs as you like.
        - A scalar entering a color port broadcasts to all three channels. A
          color entering a scalar port narrows to its luma.

        ## Normalled sockets: wires you do not have to draw

        Some sockets are already carrying a signal with nothing patched into
        them. `describe_patch` writes them as
        `in <- Time (normalled, no wire)`, and there is no module on the
        canvas to see: one hidden Time and one hidden Coordinates are shared
        by the whole patch.

        - **`in` on every oscillator and every sequencer is normalled to
          Time.** So an oscillator you place and never wire is already
          oscillating, and a sequencer you place and never wire is already
          playing. This is the common case and needs no work from you.
        - **`x` and `y` on every Space, Pattern and Feedback module are
          normalled to Coordinates.** So Rotate, Tile, Noise, Rings, Checker
          and Feedback already read the pixel's own position.
        - **A wire overrides the normal**, exactly as a wire overrides a
          knob. Pull the wire and the normal comes back.
        - **A normalled socket has no knob.** `set_knobs` on one is refused:
          the value would never be read. If what you want there really is a
          constant, patch a **Value** module in — then the patch shows it.

        What is *not* normalled, and still has to be wired if it should move:

        - Noise's `z`, Rings' `offset`, an angle on Rotate, a `dx`/`dy` on
          Translate: wire **Time** into these to make a picture move.
        - Anything expecting a sound: a Filter's `in`, a Delay's `in`, the
          Output's `left` and `right`.

        ## Why `in` still matters

        It is the domain a module is read across, and what is on it decides
        what the module does:

        - An oscillator accumulates `(in - in_before) x freq`, so its pitch
          is how fast `in` moves multiplied by `freq`. Time moves at one
          second per second, which is why `freq` on a Time-driven
          oscillator is the frequency it says it is. **Do not put a
          Multiply between Time and `in` to slow a tone down** — that
          divides the pitch and leaves the knob lying. A 440 Hz oscillator
          fed a fifth of a second per second is an 88 Hz oscillator with a
          knob that says 440.
        - A sequencer is on whichever step its `in` has reached, at `rate`
          steps per unit of `in`.
        - Patch a **Coordinates** output into `in` to draw with it instead
          of playing it — `x` for upright bands, `y` for flat ones,
          `radius` for rings. That is the one common reason to wire `in` at
          all.
        - Patch a constant in — a **Value** — to deliberately hold a module
          still. It compiles fine and is a still picture, which is
          sometimes what is wanted.
        - **To slow a picture down**, put a Multiply after Time and wire it
          in. Time itself is seconds and nothing else, so the place a patch
          runs slowly is visible in the patch.

        ## Sinks

        - A patch needs a **Video Output** to show anything and an **Audio
          Output** to play anything. Either on its own is a whole patch: one
          for the screen is silent, one for the speakers draws nothing, and
          neither of those is a mistake. Nothing will nag you about the one
          you left out.
        - A patch with **neither** does nothing at all. That is the one case
          the compiler remarks on, and it will not be proposed.
        - A patch has **at most one of each**. There is one screen and one
          pair of speakers, so a second of either is refused: compilation
          starts at one sink and never reaches another, which would leave
          you with a patch that compiles and half a patch that plays. To
          send several things to the screen, mix them into the one Video
          Output you have.
        - The two are compiled separately from one graph, and each pays only for
          the modules it actually reaches. A noise field feeding the screen
          costs the speakers nothing.

        ## Feedback

        A value cannot depend on itself within one evaluation, so every loop needs
        something in it that remembers. Wiring a cycle with nothing of the kind in
        it is an error and the tools will refuse it.

        - To read the previous *frame*, use the `feedback` module, which is an
          explicit one-frame delay. Route it back towards the output through a
          `space.rotate` or `space.scale` for the camera-pointed-at-its-own-monitor
          tunnel. This is the one to reach for on the screen.
        - To close a loop the way a modular rack does, put a `feedback.unit`
          anywhere in it. It is one evaluation of delay, it is the only module a
          wire may run backwards into, and it makes the cycle legal — an
          oscillator into its own phase, a filter into its own input. Audio only:
          a picture is drawn all at once and has no previous evaluation to read.

        ## What you can check

        Every edit you make comes back with the compiler's current complaints,
        so you do not need to ask. You can also `render` the patch and look at
        the result — that is the only way to find out whether it is *anything*,
        as opposed to merely legal.

        """;

    /// <summary>
    /// What to say about the sound, which is the one thing the briefing cannot
    /// state without knowing how this run is configured.
    /// </summary>
    /// <remarks>
    /// Both halves are worth their place. A model told nothing would assume it
    /// can hear — every other tool it has answers when called — and would
    /// describe a sound it never heard. A model that <em>can</em> hear has to be
    /// told to, because the loop it already knows is build, render, look, and a
    /// patch with no picture in it offers nothing to look at.
    /// </remarks>
    private const string Deaf = """
        You cannot hear the sound. If the patch makes noise, reason about it
        from the modules and say plainly that you have not heard it.

        """;

    private const string Hearing = """
        You have an ear, though it is not yours. `listen` renders a stretch of
        the sound, measures it, and plays it to a second model that can hear.
        What comes back is two different kinds of thing, and the difference
        between them matters more than anything else about this tool.

        **The measurements are facts.** Peak, rms, crest and the level across
        the clip are computed from the samples. Crest — peak above rms — is
        the one to read first: near 3 dB is a steady tone, and 12 dB or more
        means there are hits in it. The row of slice levels is the same
        question over time: near-identical figures are something continuous,
        and a rhythm moves.

        **The description is one listener's opinion**, and it is not told what
        the patch is or what you were trying to build — deliberately, so that
        it can disagree with you. Treat it as evidence, not as a verdict, and
        say where a claim about the sound came from when you repeat it.

        **Where the two disagree, the measurements win.** A description of
        drums over a crest of 6 dB is wrong, whatever it says, because a clip
        with drum hits in it cannot measure that way. Say so and go and look
        at the patch. This happens: a listener handed a plain drone will
        sometimes find the thing you were hoping for in it.

        Use `listen` on any patch wired to the Output's `left` or `right`, the
        way you use `render` on one wired to `color`. Silence never reaches
        the ear at all — it comes back as a sentence saying so, and it usually
        means something on the way to the Output holds still.

        """;

    private const string Working = """
        ## How to work

        Call `describe_patch` first to see what is already there. Build with
        `add_module`, `connect` and `set_knobs`; read the issues that come back;
        check it when the shape is right and adjust what you found. When you are
        happy, call `propose` with a one-line summary. Nothing you do reaches
        the person's editor until they accept that proposal, so work freely.

        You do not have to end on a proposal. If what was asked for is unclear,
        or there is a choice only the person can make, say so and stop —
        that ends your turn and they will answer. This is a conversation and
        it keeps everything you have built, so asking is cheaper than guessing
        at something they will have to undo.

        # The modules

        Format: `type id | name | category`, then inputs and outputs as
        `index name`, then what it is for. A knob's default and range follow its
        name. `~` marks a color port, `*` a port that takes whatever is plugged
        in, `->n` an input that falls back to input `n` when nothing is wired to
        it, and `note` a knob that reads as a note name rather than a number.
        `<-Module` in place of a default marks a normalled input: it has no knob
        and is already reading that module with no wire.
        A line that is neither an input nor an output means the module carries
        something that is not a knob at all — a tune, a scale, a file — and it
        names the tool that writes it.

        """;

    /// <summary>
    /// The whole briefing. <paramref name="prose"/> false drops each module's
    /// description, which is what makes a large catalogue fit — see
    /// <see cref="ProseBudget"/>.
    /// </summary>
    /// <param name="prose"></param>
    /// <param name="hearing">
    /// Whether this run has the <c>listen</c> tool. It changes one paragraph,
    /// and it has to change it: the briefing is the only place the model is told
    /// what it can check, and being wrong about that either wastes a tool it has
    /// or invents a sound it does not.
    /// </param>
    /// <param name="modules"></param>
    public static string Render(ModuleCatalog modules, bool prose, bool hearing = false)
    {
        var text = new StringBuilder(Conventions)
            .Append(hearing ? Hearing : Deaf)
            .Append(Working);

        // Catalogue order, not sorted: it is already deterministic (built-ins in
        // declaration order, then each plugin in load order) and re-sorting here
        // would be one more thing that could quietly stop matching itself.
        foreach (var def in modules.All)
        {
            Describe(text, def, modules, prose);
            text.AppendLine();
        }

        return text.ToString();
    }

    private static void Describe(StringBuilder text, NodeDef def, ModuleCatalog modules, bool prose)
    {
        text.Append(def.TypeId).Append(" | ").Append(def.Name).Append(" | ").Append(def.Category);

        // Only where it is not both, because both is nearly everything and a line
        // saying so on every module would bury the handful it matters for.
        if (def.Sinks is not ModuleSinks.Both)
            text.Append(" | ").Append(def.Sinks is ModuleSinks.Audio ? "audio only" : "video only");

        if (modules.ProviderOf(def.TypeId) is { } from && from.Id != NodeCatalog.BuiltInProvider.Id)
            text.Append(" | from the ").Append(from.Name).Append(" plugin");

        text.AppendLine();

        Sockets(text, "in ", def.Inputs, modules, knobs: true);
        Sockets(text, "out", def.Outputs, modules, knobs: false);

        // Said per module rather than only in the preamble, because this is the
        // one place a model looks to find out what a module has — and a
        // sequencer's inputs say nothing about the tune it plays.
        foreach (var extra in def.Extras) text.AppendLine(Vocabulary.Announce(extra));

        if (prose && def.Description.Length > 0)
            text.Append("  ").AppendLine(def.Description);
    }

    private static void Sockets(
        StringBuilder text,
        string label,
        IReadOnlyList<PortSpec> ports,
        ModuleCatalog modules,
        bool knobs)
    {
        text.Append("  ").Append(label);

        if (ports.Count == 0)
        {
            text.AppendLine("  (none)");
            return;
        }

        for (var i = 0; i < ports.Count; i++)
        {
            var port = ports[i];

            text.Append("  ").Append(i).Append(' ');

            if (port.Kind == PortKind.Color) text.Append('~');
            else if (port.Kind == PortKind.Any) text.Append('*');

            text.Append(port.Name);

            if (!knobs) continue;

            // A normalled socket has no knob and no range worth printing: it
            // reads the module named here until something is patched in, and a
            // default beside it would read as a number that could be set.
            if (modules.Normalled(port) is { } source)
            {
                text.Append("<-").Append(source.Replace(' ', '.'));
                continue;
            }

            text.Append('=').Append(Number(port.Default));
            text.Append(" [").Append(Number(port.Min)).Append("..").Append(Number(port.Max)).Append(']');

            if (port.NormalledFrom >= 0) text.Append("->").Append(port.NormalledFrom);
            if (port.Display == PortDisplay.Note) text.Append(" note");

            // Said because the number is not the quantity: a patch that wants a
            // fiftieth of a second here has to write -1.7, and an agent told
            // only the range would write 0.02 and be three decades out.
            if (port.Display == PortDisplay.Duration) text.Append(" log10-seconds");
        }

        text.AppendLine();
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
