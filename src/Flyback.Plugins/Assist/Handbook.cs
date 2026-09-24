using System.Globalization;
using System.Text;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The synth as a model should be told it: the conventions the catalog cannot
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
    /// What the catalog cannot say about itself. Hand-written, and the place
    /// to state a convention that lives in an ADR rather than in a type.
    /// </summary>
    private const string Conventions = """
        # Flyback

        A patchable synthesizer for picture and sound. Nothing is drawn: every
        frame is a function evaluated once per pixel, and every sample is the
        same function evaluated once per tick. You build that function by
        placing modules and wiring them together.

        ## Coordinates and values

        - `y` runs -1 at the bottom to 1 at the top.
        - `x` is the same scale widened by the aspect ratio, about -1.78 to
          1.78 on 16:9, so `Length(x, y)` is a true radius. Coordinates'
          `aspect` is that 1.78, for reaching the edge of the frame.
        - `t` is seconds since the patch started, from the **Time** module or
          a socket normalled to it.
        - The screen takes 0..1 per channel, clamped, with no gamma: 0.5 is mid
          gray, 4 is as white as 1, -1 is black. There is no headroom to pull
          back down later.

        ## The language

        A patch is written as text. `write_patch` takes the whole of one, and
        `describe_patch` gives it back under the handles the editing tools
        answer to.

        ```
        let slowly = t * 0.2

        let wave = y |> sine(freq: 1.1, phase: slowly)

        x |> sine(freq: 1.5)
          |> add(a: _, b: wave)
          |> remap(-2..2, 0..1)
          |> color.hsv(hue: _, saturation: 0.85, value: 1)
          |> out.color
        ```

        - **`|>` is a wire** into the module on the right. A module is named
          by the last part of its type id, `space.kaleidoscope` as
          `kaleidoscope`, except `color.hsv`, `color.mix`, `math.mix` and
          `midi.in`, which are written in full.
        - **Where the signal lands**: a socket called `in`, or a module's only
          socket; failing that a leading `x` and `y`, two signals at once,
          which is how Space and Pattern modules chain; failing that, for a
          color, the module's one color socket, so `hsv(...) |> gain(gain: 2)`
          needs nothing more. Anywhere else, say which socket with `_`:
          `beat.gate |> adsr(gate: _, decay: 240ms)`. A module with neither
          and no `_` is refused.
        - **A pipeline is a statement, never an argument.** Bind it with `let`
          and name it in the call.
        - **Sockets are named arguments**, a space written as an underscore:
          `remap(in_low: -1, out_high: 1)`. A socket not named keeps its
          default.
        - **`let` names a signal.** Reading it twice is two wires out of one
          module, not two modules. A name is bound once, a socket takes one
          wire, and a knob is set once.
        - **`out` is the Output** every patch already has: `|> out.color`,
          `|> out.left`, `out.volume = 0.6`.
        - **Sugar**: `x`, `y`, `radius`, `angle`, `aspect` and `t` are
          Coordinates and Time, one shared module each, and cannot be bound
          to anything else. `+ - * / %` are the
          maths modules. `A3` and `C#4` are notes, on sockets that read notes.
        - **A length of time is written as one**: `attack: 10ms`, `1.5s`.
          These sockets hold a power of ten, so a bare number is refused.
        - **A tune or a scale is a block** after the call:
          `notes(rate: 4) [ A3 C4 [E4 G4] ~ ]`, `quantiser() [ C D E G A ]`.
          `~` is a rest, `[a b]` splits a step in two, `@3` makes a step three
          times as long, `!3` repeats it, `<a b>` alternates each pass,
          `a(3,8)` is three hits over eight steps, and `E5%0` is the note
          silenced, which is not a rest.
        - **A file is a string**: `sample("kick.wav")`, `picture("photo.png")`.
        - **`off name`**, on its own line, switches a module off: what is
          patched into it comes straight out, and nothing where nothing is.
          `switch_module` does it to a patch that exists, and a patch you are
          shown keeps the line only if you write it again.
        - **`keyboard scale [ C D E G A ]`**, on its own line, lays the
          computer keyboard out in that scale for a MIDI In. Leave it out for
          a piano.
        - **`description "What the patch is for."`** goes first, with
          **`author "Who made it"`** and **`tags "drone" "slow"`** under it,
          each on its own line.
        - **A loop is a wire that runs backwards**, the one thing `|>` cannot
          say:

        ```
        let sum = square(freq: 110) * 0.06 |> add(a: _)

        sum.b <- sum * 0.94
        sum |> out.left
        ```

        Nothing is adopted unless all of it reads, and a mistake comes back
        with its line and column. `keyboard`, `description`, `author` and
        `tags` each go once in a patch.

        ## Putting a patch together

        - Every input is a knob. Wire only the ones that need to move.
        - A wire overrides the knob, which comes back if the wire goes.
        - An input takes one wire; a second replaces the first. An output fans
          out to any number.
        - A scalar into a color port fills all three channels; a color into a
          scalar port narrows to its luma.

        ## Normalled sockets

        Some sockets already carry a signal with nothing patched in.
        `describe_patch` writes them as `in <- Time (normalled, no wire)`; one
        hidden Time and one hidden Coordinates serve the whole patch.

        - **`in` on every oscillator and sequencer is normalled to Time**, so
          one placed and never wired is already oscillating or playing.
        - **`x` and `y` on every Space, Pattern and Feedback module are
          normalled to Coordinates**, so they already read the pixel's own
          position.
        - A wire overrides the normal, and pulling it brings the normal back.
        - **A normalled socket has no knob**, and `set_knobs` on one is
          refused. For a constant there, patch in a **Value**.

        Not normalled, so wire **Time** in to make a picture move: Clouds'
        `z`, Rings' `offset`, Rotate's angle, Translate's `dx` and `dy`. Nor is
        anything that expects a sound: a Filter's or a Delay's `in`, the
        Output's `left` and `right`.

        ## What `in` does

        - An oscillator's pitch is how fast `in` moves times `freq`. Time moves
          a second per second, so `freq` is the frequency it says. **Do not
          slow a tone with an Expression like `a * 0.2` between Time and
          `in`**: a 440 Hz oscillator fed that plays 88 Hz with a knob that
          says 440.
        - A sequencer is on whichever step `in` has reached, at `rate` steps
          per unit of `in`.
        - Patch a **Coordinates** output into `in` to draw with a module
          instead of playing it: `x` for upright bands, `y` for flat ones,
          `radius` for rings. That is the common reason to wire `in`.
        - A **Value** in `in` holds a module still, which is a still picture.
        - **To slow a picture down**, put the Expression after Time and wire
          that in, so where the patch runs slowly is visible in it.

        ## The Output

        - **Every patch has exactly one Output.** It cannot be added or
          removed; the work is wiring into it. To show several things, mix
          them into its one `color`.
        - **`color` is the picture; `left` and `right` are the sound.**
          `right` is normalled to `left`, so patch it only when the sides
          differ. `volume` at nought switches the speakers off. The picture
          is heard by reading it through a **Scan** into `left`.
        - A patch with nothing in `color` draws black, and one with nothing in
          `left` is silent; both are whole patches. A patch with nothing
          reaching the Output at all is not proposed.
        - Picture and sound are compiled apart, each paying only for the
          modules it reaches, so a noise field on the screen costs the
          speakers nothing.

        ## Feedback

        A value cannot depend on itself within one evaluation, so a loop needs
        something that remembers, and one without is refused.

        - The `feedback` module reads the previous *frame* anywhere in it.
          Route it back through a `space.rotate` or `space.scale` for the
          camera-at-its-own-monitor tunnel. This is the one for the screen.
        - A wire that closes a cycle carries the evaluation before: a sample
          to the ear, each pixel's own last value to the eye. So an oscillator
          into its own phase or a filter into its own input is just wired.

        ## What you can check

        Every edit comes back with the compiler's complaints. `render` shows
        the result, which is the only way to find out whether it is
        *anything* rather than merely legal.

        """;

    /// <summary>
    /// What to say about the sound, which is the one thing the briefing cannot state
    /// without knowing how this run is configured.
    /// </summary>
    /// <remarks>
    /// A model told nothing would assume it can hear and describe a sound it never
    /// heard; one that can hear has to be told to, because the loop it knows is
    /// build, render, look. And a model that hears the clip itself needs a different
    /// warning from one handed somebody else's account: a borrowed ear agrees with
    /// whoever asked it, and one's own ear agrees with whoever built the patch.
    /// </remarks>
    private const string Deaf = """
        You cannot hear the sound. If the patch makes noise, reason about it
        from the modules and say plainly that you have not heard it.

        """;

    private const string Secondhand = """
        You have an ear, though it is not yours. `listen` renders a stretch of
        the sound, measures it, and plays it to a second model that can hear.

        **The measurements are facts**, computed from the samples. Read crest,
        peak above rms, first: near 3 dB is a steady tone, 12 dB or more has
        hits in it. Near-identical slice levels are something continuous; a
        rhythm moves.

        **The description is one listener's opinion.** It is not told what the
        patch is for, so that it can disagree with you. Treat it as evidence,
        and say where a claim about the sound came from when you repeat it.

        **Where the two disagree, the measurements win.** Drums described over
        a crest of 6 dB are wrong, because a clip with hits cannot measure that
        way. Say so and look at the patch: a listener handed a drone will
        sometimes hear what you hoped for in it.

        `listen` is for a patch wired to `left` or `right`, as `render` is for
        `color`. Silence comes back as a sentence, and usually means something
        on the way to the Output holds still.

        """;

    private const string FirstHand = """
        You can hear. `listen` renders a stretch of the sound, measures it, and
        plays you the clip, which arrives after the tool's reply the way a
        rendered frame does.

        **The measurements are facts**, computed from the samples. Read crest,
        peak above rms, first: near 3 dB is a steady tone, 12 dB or more has
        hits in it. Near-identical slice levels are something continuous; a
        rhythm moves.

        **What you hear is your own impression of a patch you built**, which
        already knows what it was hoping for. Say what is there rather than
        what it was for: a patch nearly the thing you intended sounds, to you,
        like the thing you intended.

        **Where the two disagree, the measurements win.** Drums you hear over a
        crest of 6 dB are not drums, because a clip with hits cannot measure
        that way. Say so and look at the patch. Nothing else here can
        contradict you, so when the numbers do, that is the finding.

        `listen` is for a patch wired to `left` or `right`, as `render` is for
        `color`. Silence is never played; it comes back as a sentence, and
        usually means something on the way to the Output holds still.

        """;

    private const string Working = """
        ## How to work

        Call `describe_patch` first to see what is already there.

        **Then write the patch with `write_patch`, in one call.** Placing a
        module or a wire is a call each, so building that way runs out of turn
        before a large patch is done. Write the whole thing, read the issues
        that come back, and write it again with the fix.

        **Change what is there and leave the rest alone.** When the bench is
        not empty and the person asked for a change, start from
        `describe_patch`. A few modules is `add_module`, `connect`,
        `set_knobs` and `remove_module`; many is `write_patch` with the
        description altered only where asked, which gives every module a new
        identity. If the change needs something else built, say so and stop:
        a different patch that resembles the request is not the request.

        **Say what you did.** The proposal's summary names what you added,
        removed or rewired, and anything not asked for. If the request assumed
        something the patch lacks, say that first, then what you did about it.

        **Keep the sum out of clipping.** Voices add, and past 1 they distort.
        Where you can measure the peak and it is above -1 dBFS, lower the
        levels before proposing; about -6 dBFS is comfortable. Where you
        cannot, scale the voices so they cannot add past 1. Bring down the
        peak, costing about as much loudness as the overshoot.

        **Do not invent a filename.** A Sample naming a file that is not on the
        person's disk never compiles, and the whole patch is refused. Unless
        they gave a path, build the sound: a drum is an envelope shaping an
        oscillator, and a kick adds a second envelope dropping its pitch.

        ## Making something rhythmic

        Three mistakes turn a beat into one continuous tone. All compile, and
        none sounds wrong so much as absent.

        - **An envelope opens on a gate, and a sequencer's gate is `.gate`.**
          `steps |> adsr(gate: _)` sends the note number, which never closes
          the envelope. Write `steps.gate |> adsr(gate: _)`; the bare name is
          the pitch, and it reaches `freq` by way of a Note.
        - **A step's rate is steps per second**, so a sequencer left at 1
          plays two steps in a two-second clip. Patch a Tempo into `rate`, or
          set how many steps a second you want. An envelope whose decay
          outlasts its step is a drone.
        - **An oscillator with nothing in `freq` sits at 1 Hz**, below
          hearing, and an envelope on its `amp` does not give it a pitch. A
          tune reaches `freq` through a Note: `let pitch = seq |> note(note: _)`,
          then `saw(freq: pitch)`. Nothing warns about this one.

        A kick and a bass line, whole:

        ```
        let beat  = values(rate: 4) [ 1 ~ 1 ~ ]
        let level = beat.gate |> adsr(gate: _, attack: 1ms, decay: 240ms, sustain: 0, release: 80ms)
        let drop  = beat.gate |> adsr(gate: _, attack: 0.5ms, decay: 40ms, sustain: 0, release: 16ms)
        let sweep = drop |> remap(0..1, 47..205)
        let kick  = sine(freq: sweep) * level

        let line  = notes(rate: 4) [ A1 A1 E2 A1 ]
        let pitch = line |> note(note: _)
        let pluck = line.gate |> adsr(gate: _, attack: 2ms, decay: 120ms, sustain: 0.3, release: 60ms)
        let bass  = saw(freq: pitch, amp: 0.8) * pluck

        mixer(kick, 1, bass, 0.7) |> out.left
        ```

        ## Proposing

        Check the result when the shape is right, adjust, then call `propose`
        with a one-line summary. Nothing reaches the person's editor until
        they accept it, so work freely, and a turn that ends without a
        proposal shows them nothing: do not describe what they can see or
        call it ready unless you proposed it.

        **Never ask whether to propose.** If you built what was asked, propose
        it and say what you would tune next. It commits nothing: one keystroke
        puts their patch back, and yours stays on the bench. Asking costs them
        a turn to say yes to something that was never a decision.

        The one reason not to propose is a choice only they can make: two
        readings of the request, a key, a tempo with nothing to base a guess
        on. Ask it, and say the patch is not applied yet. "Is this good?" is
        not such a choice.

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
    /// What the list of presets is given out of the budget before the modules divide
    /// the rest, whether or not there are any. Fixed rather than measured, so that
    /// which modules lose their descriptions depends on the modules alone and the
    /// canvas can mark them with no conversation to ask; the shipped presets take
    /// 2,220 of it and a name is all a saved one costs.
    /// </summary>
    internal const int PresetsReserve = 4_000;

    private const string PresetsPreamble = """
        # Presets

        Whole patches that are already built, the ones this instrument ships and
        whatever the person has saved. `describe_preset` gives one in the
        language, to read how it works — how a filter is made out of what there
        is, how a picture is tied to a tune. Take the idea and build what was
        asked for; do not hand a preset back as the answer.

        """;

    private const string PresetsUnexplained = """
        Some presets below have no description line. They have one all the same,
        left out to keep this list short: `describe_preset` gives it.

        """;

    private const string PresetsUnnamed = """
        Not every preset is named below either: `describe_preset` asked for a
        name that is not one answers with all of them.

        """;

    /// <summary>
    /// The presets a model may read, one line each, or nothing where there are none.
    /// </summary>
    /// <remarks>
    /// Held to the same budget as the modules, and after them: descriptions are cut
    /// first and names after them, each kept in list order while it fits, and whatever is
    /// left out is said so. Never longer than <paramref name="room"/> unless the
    /// preamble and those notes alone are, since the modules' share cannot know how
    /// many presets somebody has saved.
    /// </remarks>
    /// <param name="presets"></param>
    /// <param name="room">
    /// What is left of the briefing's budget after everything before this, which may
    /// be nothing at all.
    /// </param>
    internal static string Presets(IReadOnlyList<PatchPreset> presets, int room)
    {
        if (presets.Count == 0) return string.Empty;

        room -= PresetsPreamble.Length;

        var names = presets.Sum(Named);
        var all = presets.Sum(Described);

        var left = names + all > room;
        var unnamed = left && names + PresetsUnexplained.Length > room;

        if (left) room -= PresetsUnexplained.Length;
        if (unnamed) room -= PresetsUnnamed.Length;
        else room -= names;

        var named = new bool[presets.Count];
        var described = new bool[presets.Count];

        for (var i = 0; i < presets.Count; i++)
        {
            if (unnamed)
            {
                if (Named(presets[i]) > room) continue;

                named[i] = true;
                room -= Named(presets[i]);
            }
            else
            {
                named[i] = true;

                var cost = Described(presets[i]);

                if (cost == 0 || cost > room) continue;

                described[i] = true;
                room -= cost;
            }
        }

        var text = new StringBuilder(PresetsPreamble);

        if (left) text.Append(PresetsUnexplained);
        if (unnamed) text.Append(PresetsUnnamed);

        for (var i = 0; i < presets.Count; i++)
        {
            if (!named[i]) continue;

            text.Append(presets[i].Name);

            if (described[i]) text.Append(" | ").Append(presets[i].Description);

            text.AppendLine();
        }

        return text.ToString();

        static int Named(PatchPreset preset) => preset.Name.Length + Environment.NewLine.Length;

        // What a line adds for a description: the separator and the text.
        static int Described(PatchPreset preset) =>
            preset.Description.Length == 0 ? 0 : 3 + preset.Description.Length;
    }

    /// <summary>
    /// Said only when some module's description was left out, since otherwise
    /// a module with none would read as a module with nothing to say.
    /// </summary>
    private const string Unexplained = """
        Some modules below have no description line. They have one all the
        same, left out to keep this list short: `describe_module` gives it, and
        `find_modules` searches every description.

        """;

    /// <summary>The whole briefing, with every description but <paramref name="undescribed"/>'s.</summary>
    /// <param name="modules"></param>
    /// <param name="undescribed">
    /// Type ids whose descriptions are left out — see <see cref="ProsePolicy"/>.
    /// </param>
    /// <param name="hearing">
    /// Whether this run has the <c>listen</c> tool, and whose ear answers it. The
    /// briefing is the only place the model is told what it can check, and being
    /// wrong about that either wastes a tool it has or credits its own impression to
    /// a listener that was never there.
    /// </param>
    public static string Render(ModuleCatalog modules, IReadOnlySet<string> undescribed, Listener hearing = Listener.None)
    {
        var text = new StringBuilder(Conventions)
            .Append(hearing switch
            {
                Listener.Itself => FirstHand,
                Listener.Another => Secondhand,
                _ => Deaf,
            })
            .Append(Working);

        if (undescribed.Count > 0) text.Append(Unexplained);

        // Catalog order, not sorted: it is already deterministic (built-ins in
        // declaration order, then each plugin in load order) and re-sorting here
        // would be one more thing that could quietly stop matching itself.
        // The Maths modules an Expression stands for are left out: asked for, they
        // arrive as one (ADR-0109), and their names are its functions.
        foreach (var def in modules.All.Where(def => !ExpressionFusion.Retired(def)))
        {
            Describe(text, def, modules, prose: !undescribed.Contains(def.TypeId));
            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>
    /// Which descriptions <paramref name="policy"/> leaves out of the briefing for
    /// <paramref name="modules"/> — see <see cref="ProsePolicy"/>.
    /// </summary>
    /// <remarks>
    /// Measured against the longest of the three things the briefing can say about
    /// hearing, so that the answer does not depend on which model is asked: the
    /// canvas marks these modules long before any conversation has a listener.
    /// </remarks>
    internal static IReadOnlySet<string> Undescribed(ModuleCatalog modules, ProsePolicy policy)
    {
        var described = modules.All.Where(def => def.Description.Length > 0 && !ExpressionFusion.Retired(def)).ToArray();
        var everyone = described.Select(def => def.TypeId).ToHashSet(StringComparer.Ordinal);

        if (everyone.Count == 0) return everyone;

        // What the briefing costs with every description left out, the note saying
        // so included.
        var fixedCost = new[] { Listener.None, Listener.Another, Listener.Itself }
            .Max(hearing => Render(modules, everyone, hearing).Length);

        var room = policy.Budget - PresetsReserve - fixedCost;

        if (described.Sum(Cost) <= room + Unexplained.Length) return new HashSet<string>(StringComparer.Ordinal);

        room -= described.Where(def => policy.Priority.Contains(def.TypeId)).Sum(Cost);

        var left = new HashSet<string>(StringComparer.Ordinal);

        foreach (var def in described)
        {
            if (policy.Priority.Contains(def.TypeId)) continue;

            var cost = Cost(def);

            if (cost <= room) room -= cost;
            else left.Add(def.TypeId);
        }

        return left;

        // What Describe adds for a description: the indent, the text and the line end.
        static int Cost(NodeDef def) => 2 + def.Description.Length + Environment.NewLine.Length;
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
