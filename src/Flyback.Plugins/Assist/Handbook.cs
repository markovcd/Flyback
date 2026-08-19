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
        - `t` is seconds since the patch started. It is not ambient — it
          reaches a patch through the **Time** module and nowhere else.
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

        ## The `in` socket, and why a patch sits still

        Oscillators and sequencers have an `in`. It is the domain they are
        read across, not an optional extra, and **nothing drives it for
        you**. Left on its knob it is a constant, and a constant domain is a
        patch that does not move:

        - An oscillator accumulates `(in - in_before) x freq`. If `in` never
          changes, the phase never advances and the output is one fixed
          value — silence at the speakers, a flat field on the screen. Note
          that `freq` alone does nothing about this; a frequency multiplied
          by no movement is no movement.
        - A sequencer is on whichever step its `in` has reached. If `in`
          never changes it stays on step 1 forever.

        So wire it:

        - **For sound, patch Time's `t` into `in`.** That is what makes an
          oscillator oscillate and a sequencer play. A melody needs Time
          into the sequencer's `in` *and* into the oscillator's `in`.
          Straight in, unscaled: `freq` is the pitch, and anything that
          slows the domain down divides that pitch by the same amount. A
          440 Hz oscillator fed a fifth of a second per second is an 88 Hz
          oscillator with a knob that says 440.
        - **To slow a picture down**, put a Multiply after Time — 0.2 for a
          fifth of the speed. Time itself is seconds and nothing else, so
          the place a patch runs slowly is visible in the patch.
        - **For a picture**, patch a Coordinates output — `x` for upright
          bands, `y` for flat ones, `radius` for rings — or Time, for
          something that moves without varying across the frame.

        This is the most common way to build a patch that reads correctly,
        compiles without a single complaint, and does nothing at all. You
        cannot hear the result, so check it by eye: every oscillator and
        every sequencer should have a wire into `in`.

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
        the sound, measures it, and plays it to a second model that can hear —
        what comes back to you is that model's description in words, along with
        the peak and rms levels, which are measured from the samples rather
        than described. Use it on any patch wired to the Output's `left` or
        `right`, the way you use `render` on one wired to `color`: a patch
        built for the speakers has nothing to look at, and this is the only
        check it has.

        Two things follow from the description being second-hand. It answers
        what you tell it you are listening for, so put that in `note` — "is
        the bass tone clean or buzzing" gets a better answer than a bare call
        does. And it is one listener's account rather than the sound itself,
        so say where a claim about the sound came from, and trust the measured
        levels over the prose where the two disagree.

        Silence never reaches the ear at all: it comes back as a sentence
        saying so, and it usually means something on the way to the Output
        holds still.

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
        A `notes` line means the module carries a list of notes instead of step
        knobs — write it with `set_steps`.

        """;

    /// <summary>
    /// The whole briefing. <paramref name="prose"/> false drops each module's
    /// description, which is what makes a large catalogue fit — see
    /// <see cref="ProseBudget"/>.
    /// </summary>
    /// <param name="hearing">
    /// Whether this run has the <c>listen</c> tool. It changes one paragraph,
    /// and it has to change it: the briefing is the only place the model is told
    /// what it can check, and being wrong about that either wastes a tool it has
    /// or invents a sound it does not.
    /// </param>
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

        if (modules.ProviderOf(def.TypeId) is { } from && from.Id != NodeCatalog.BuiltInProvider.Id)
            text.Append(" | from the ").Append(from.Name).Append(" plugin");

        text.AppendLine();

        Sockets(text, "in ", def.Inputs, knobs: true);
        Sockets(text, "out", def.Outputs, knobs: false);

        // Said per module rather than only in the preamble, because this is the
        // one place a model looks to find out what a module has — and a
        // sequencer's inputs say nothing about the tune it plays.
        if (def.DefaultSteps is not null)
            text.Append("  notes  a list of up to ").Append(NodeCatalog.MaxSteps)
                .AppendLine(", set with set_steps — not knobs");

        if (prose && def.Description.Length > 0)
            text.Append("  ").AppendLine(def.Description);
    }

    private static void Sockets(StringBuilder text, string label, IReadOnlyList<PortSpec> ports, bool knobs)
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
