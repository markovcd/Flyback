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

        A patchable synthesiser for picture and sound. Nothing is drawn: every
        frame is a function evaluated once per pixel, and every sample is the
        same function evaluated once per tick. You build that function by
        placing modules and wiring them together.

        ## Coordinates and values

        - `y` runs -1 at the bottom to 1 at the top.
        - `x` is the same scale widened by the aspect ratio, so it runs about
          -1.78 to 1.78 on a 16:9 frame. That is what keeps circles circular:
          `Length(x, y)` is a true radius. Coordinates' `aspect` is that 1.78,
          for reaching the edge of whatever frame is being drawn.
        - `t` is seconds since the patch started. It reaches a patch through
          the **Time** module, or through a socket normalled to it — see
          below.
        - What reaches the screen is 0..1 per channel, clamped, with no gamma. A
          value of 0.5 is mid gray; 4 and 1 are the same white; -1 is black.
          There is no headroom to pull back down later.

        ## The language

        A patch is written as text, and `write_patch` takes the whole of one in
        a single call. `describe_patch` gives it back in the same language,
        under the same handles the editing tools answer to. **Build a patch by
        writing it; change one that already exists with `set_knobs` and
        `connect`** — writing a patch afresh gives every module a new identity
        and loses where they sit on the canvas.

        ```
        let slowly = t * 0.2

        x |> sine(freq: 1.5)
          |> add(y |> sine(freq: 1.1, phase: slowly))
          |> remap(-2..2, 0..1)
          |> color.hsv(saturation: 0.85, value: 1)
          |> out.color
        ```

        - **`|>` is a wire.** What is on the left goes into the module on the
          right. A module is named by the last part of its type id, so
          `space.kaleidoscope` is `kaleidoscope` — except `hsv`, `mix` and
          `midi.in`, which have to be written in full: `color.hsv`,
          `color.mix` or `math.mix`, `midi.in`.
        - **Where the signal lands**: a socket called `in` takes it; failing
          that a leading `x` and `y` take a position, two signals at once, which
          is how Space and Pattern modules chain; failing that the first socket
          the call did not name.
        - **Sockets are named arguments**, with a space written as an
          underscore: `remap(in_low: -1, out_high: 1)`, `gate_length`. A socket
          you say nothing about keeps its default.
        - **`let` names a signal** so it can be used twice. Reading it again is
          a second wire out of the same module, not a second module.
        - **`out` is the Output**, which every patch already has:
          `|> out.color`, `|> out.left`, `out.volume = 0.6`.
        - **Sugar**: `x`, `y`, `radius`, `angle`, `aspect` and `t` are Coordinates and
          Time, one shared module each however often written. `+ - * / %` are
          the maths modules. `A3` and `C#4` are notes, on sockets that read
          notes. `20ms`, `1.5s` are times, on sockets that read times — and
          those sockets hold a power of ten, so writing `20ms` is the only way
          to say it without doing logarithms.
        - **A tune or a scale is a block** after the call:
          `notes(rate: 4) [ A3 C4 [E4 G4] ~ ]`, `quantiser() [ C D E G A ]`.
          Inside one, `~` is a rest, `[a b]` splits a step in two, `@3` makes a
          step three times as long, `!3` repeats it, `<a b>` alternates on each
          pass and `a(3,8)` is three sounding steps spread over eight. `E5%0` is
          that note silenced, which is not the same as a rest.
        - **A file is a string**: `sample("kick.wav")`, `picture("photo.png")`.
        - **`off name`**, on a line of its own, switches a module off: what is
          patched into it comes straight out of it, and nothing does where
          nothing is patched in. `switch_module` says the same thing to a patch
          that already exists, and a patch you are shown that has one keeps it
          only if you write the line again.
        - **`keyboard scale [ C D E G A ]`**, on a line of its own, lays the
          computer keyboard out for whoever plays a MIDI In: the notes side by
          side along each row. Once a patch; say nothing for a piano.
        - **`description "What the patch is for."`**, on a line of its own and
          first, is the patch's one line of prose. Once a patch.
        - **`author "Who made it"`** and **`tags "drone" "slow"`** go under
          it, each once a patch.
        - **A length of time is written as one.** `attack: 10ms`, not
          `attack: 0.01`. These sockets hold a power of ten, so a bare number is
          refused rather than read as a hundred times what you meant.
        - **A loop is a wire that runs backwards**, which is the one thing `|>`
          cannot say:

        ```
        let sum = square(freq: 110) * 0.06 |> add()

        sum.b <- sum * 0.94
        sum |> out.left
        ```

        Nothing is adopted unless all of it reads. A mistake comes back with the
        line and column it is on.

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
          oscillator is the frequency it says it is. **Do not put an
          Expression like `a * 0.2` between Time and `in` to slow a tone down** — that
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
        - **To slow a picture down**, put an Expression like `a * 0.2` after
          Time and wire it in. Time itself is seconds and nothing else, so the place a patch
          runs slowly is visible in the patch.

        ## Sinks

        - **There is one Output block, and every patch already has it.** You
          cannot add one and you cannot remove one, so it is never something
          to put in place first — it is there, and the work is wiring into it.
        - **`color` is the picture. `left` and `right` are the sound.** The
          same block also carries `volume`, a knob on it like any other — at
          nought it is the speakers switched off, not merely quiet. The
          picture is heard by reading it through a **Scan** into `left`.
        - **`right` is normalled to `left`**, so a voice patched into `left`
          alone is heard from both speakers. Patch `right` only when the two
          sides should differ.
        - **A patch with nothing in `color` draws black, and that is not a
          mistake.** Neither is one with nothing in `left`. A patch built for
          the eye and a patch built for the ear are both whole patches, and
          nothing will nag you about the half you did not want.
        - **Nothing reaching it at all is the one case the compiler remarks
          on**, and such a patch will not be proposed: with no wire into it
          there is nothing to see or hear.
        - To send several things to the screen, **mix them into the one
          `color` you have** rather than looking for a second block.
        - The picture and the sound are compiled separately from one graph,
          each walking back from its own sockets, and each pays only for the
          modules it actually reaches. A noise field feeding the screen costs
          the speakers nothing.

        ## Feedback

        A value cannot depend on itself within one evaluation, so every loop needs
        something in it that remembers. Wiring a cycle with nothing of the kind in
        it is an error and the tools will refuse it.

        - To read the previous *frame*, use the `feedback` module, which is an
          explicit one-frame delay. Route it back towards the output through a
          `space.rotate` or `space.scale` for the camera-pointed-at-its-own-monitor
          tunnel. This is the one to reach for on the screen.
        - To close a loop the way a modular rack does, simply wire it: the wire
          that closes a cycle carries the evaluation before — a sample to the ear
          and a frame to the eye — so an oscillator into its own phase, a filter
          into its own input, or a pixel building on what it held last frame are
          all patches you draw rather than things you have to make legal. What a
          loop carries round on the screen is each pixel's own value, where
          `feedback` reads anywhere in the frame before.

        ## What you can check

        Every edit you make comes back with the compiler's current complaints,
        so you do not need to ask. You can also `render` the patch and look at
        the result — that is the only way to find out whether it is *anything*,
        as opposed to merely legal.

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

    private const string FirstHand = """
        You can hear. `listen` renders a stretch of the sound, measures it, and
        plays you the clip — the sound arrives after the tool's reply, the way a
        rendered frame does. What comes back is two different kinds of thing,
        and the difference between them matters more than anything else about
        this tool.

        **The measurements are facts.** Peak, rms, crest and the level across
        the clip are computed from the samples. Crest — peak above rms — is
        the one to read first: near 3 dB is a steady tone, and 12 dB or more
        means there are hits in it. The row of slice levels is the same
        question over time: near-identical figures are something continuous,
        and a rhythm moves.

        **What you hear is your own impression of a patch you built**, which is
        the one account here that already knows what it was hoping for. Say what
        is there rather than what it was for. A patch that is nearly the thing
        you intended sounds, from where you are sitting, like the thing you
        intended.

        **Where the two disagree, the measurements win.** Drums you can hear
        over a crest of 6 dB are not drums, whatever they sounded like, because
        a clip with hits in it cannot measure that way. Say so and go and look
        at the patch. Nothing else in this loop can contradict you, so when the
        numbers do, that is the finding.

        Use `listen` on any patch wired to the Output's `left` or `right`, the
        way you use `render` on one wired to `color`. Silence is never played —
        it comes back as a sentence saying so, and it usually means something on
        the way to the Output holds still.

        """;

    private const string Working = """
        ## How to work

        Call `describe_patch` first to see what is already there.

        **Then write the patch with `write_patch`, in one call.** That is how a
        patch is built here. Placing a module is one call and so is every single
        wire, so building even a modest patch that way costs dozens of them and
        a large one cannot be finished at all before the turn runs out. Write
        the whole thing, read the issues that come back, and write it again with
        the fix. Rewriting is cheap — it is one call either way.

        `add_module`, `connect` and `set_knobs` are for *changing* a patch that
        already exists: a knob to turn, a wire to move. Reach for them when the
        person asks for an adjustment, not to assemble something from nothing.

        **Change what is there and leave the rest alone.** When the patch on the
        bench is not empty and the person asked for a change, start from what
        `describe_patch` gives you. A change to a few modules is `add_module`,
        `connect`, `set_knobs` and `remove_module`. A change to many is
        `write_patch` with that description altered only where you were asked,
        which gives every module a new identity and is the price of one call.
        Every module you were not asked about stays as it was. If you cannot
        make the change without building something else, say so and stop — a
        different patch that resembles the request is not the request.

        **Say what you did.** The summary you propose names what you added,
        removed or rewired, and anything you did that was not asked for. If the
        request assumed something the patch does not have — a picture where
        there is none, a part that does not exist — say that first, then say
        what you did about it.

        **Keep the sum out of clipping.** Voices add, and a sum past 1 distorts.
        If you can measure the peak and it is above -1 dBFS, lower the levels
        before you propose; about -6 dBFS is comfortable. Where you cannot
        measure it, scale the voices so they cannot add past 1. Bring down the
        peak, not the whole mix: the fix should cost about as much loudness as
        the overshoot.

        Check it when the shape is right and adjust what you found. When you are
        happy, call `propose` with a one-line summary. Nothing you do reaches
        the person's editor until they accept that proposal, so work freely.

        **Do not invent a filename.** A Sample plays a recording that has to
        exist on the person's disk, and one naming a file that is not there
        never compiles, so the whole patch is refused. Unless they gave you a
        path, build the sound instead: a drum is an envelope shaping an
        oscillator, and a kick is that with a second envelope dropping the pitch
        out from under it.

        ## Making something rhythmic

        Three mistakes turn a patch that should have a beat in it into one
        continuous tone. All three compile, and none of them sounds wrong so
        much as absent.

        - **An envelope opens on a gate, and a sequencer's gate is
          `.gate`.** Writing `steps |> adsr(...)` sends the *note number* into
          the envelope's gate — 57 is well above open, so it never closes and
          the sound never stops. Write `steps.gate |> adsr(...)`. The bare name
          is the pitch, and it belongs in `freq` by way of a Note.
        - **A time is written as a time.** `decay: 0.1` is not a tenth of a
          second, it is a second and a quarter; write `decay: 100ms`. An
          envelope whose decay outlasts its step is a drone.
        - **A step's rate is steps per second**, so a sequencer left at 1 with a
          two-second clip plays two steps. For a beat, patch a Tempo into
          `rate`, or set it to how many steps a second you want.

        - **An oscillator with nothing in its `freq` sits at 1 Hz**, which is
          below hearing. Shaping its `amp` with an envelope does not give it a
          pitch — that is a silent oscillator being switched on and off. A tune
          reaches `freq` through a Note: `saw(freq: seq |> note)`. This one
          compiles cleanly and says nothing, so nothing will warn you.

        A kick and a bass line, whole. The kick has a pitch because it is given
        one; the bass has a pitch because the sequencer's notes reach `freq`:

        ```
        let beat  = values(rate: 4) [ 1 ~ 1 ~ ]
        let level = beat.gate |> adsr(attack: 1ms, decay: 240ms, sustain: 0, release: 80ms)
        let drop  = beat.gate |> adsr(attack: 0.5ms, decay: 40ms, sustain: 0, release: 16ms)
        let kick  = sine(freq: drop |> remap(0..1, 47..205)) * level

        let line  = notes(rate: 4) [ A1 A1 E2 A1 ]
        let bass  = saw(freq: line |> note, amp: 0.8)
                      * (line.gate |> adsr(attack: 2ms, decay: 120ms, sustain: 0.3, release: 60ms))

        mixer(kick, 1, bass, 0.7) |> out.left
        ```

        **Nothing you build is on their canvas until you propose it.** They are
        looking at the patch as it was before you started, so a turn that ends
        with changes and no proposal shows them nothing at all — do not tell
        them the patch is ready to refine, or describe what they can see, unless
        you have proposed it.

        **Never ask whether to propose.** If you have built what was asked for,
        propose it and say what you would tune next. Proposing is not a
        commitment and not the end of the conversation: it is the only way they
        can see or hear the thing, one keystroke puts the patch back as it was,
        and the patch you built stays on the bench either way. Asking "shall I
        propose this?" costs them a whole turn to say yes to something that was
        never a decision.

        You do not have to end on a proposal, and there is one reason not to: a
        choice only they can make. If what was asked for is genuinely ambiguous
        — which of two readings, a key, a tempo you have nothing to base a guess
        on — say so and stop, and say in the same breath that the patch is not
        applied yet, because they cannot tell. That is a question. "Is this
        good?" is not.

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
