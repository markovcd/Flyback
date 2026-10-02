using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Easy;

/// <summary>
/// The Easy Drum's sounds, each a function of the seconds since the hit: a swept
/// sine for the drums, filtered noise for the snare and the clap, and a cluster of
/// squares for the metal.
/// </summary>
/// <remarks>
/// The drums' pitch sweeps are integrated in closed form, so every hit starts its
/// wave at nought and nothing is remembered but what the filters hold.
/// </remarks>
internal static class Kit
{
    public const string Kick = "kick";
    public const string PsyKick = "psy kick";
    public const string Snare = "snare";
    public const string Clap = "clap";
    public const string ClosedHat = "closed hat";
    public const string OpenHat = "open hat";
    public const string Tom = "tom";
    public const string Rim = "rim";
    public const string Cowbell = "cowbell";

    private const float Tau = 6.283185307179586f;

    /// <summary>The six detuned squares a drum machine's hats are made of.</summary>
    private static readonly float[] Metal = [205.3f, 304.4f, 369.6f, 522.7f, 540f, 800f];

    /// <summary>
    /// <paramref name="sound"/>, <paramref name="age"/> seconds after the hit, and the
    /// envelope it is heard through. <paramref name="tune"/> multiplies every pitch and
    /// <paramref name="length"/> every fall; <paramref name="tone"/> is 0 dark to 1 bright.
    /// <paramref name="sixteenth"/> is a sixteenth at the tempo, in seconds.
    /// </summary>
    public static (Slot Sound, Slot Envelope) Emit(
        Emitter em, string sound, Slot domain, Slot age, Slot tune, Slot length, Slot tone, Slot sixteenth)
    {
        var white = NodeCatalog.WhiteAndPink(em, domain, em.Constant(SeedOf(sound))).White;

        switch (sound)
        {
            case Snare:
            {
                var body = em.Add(
                    em.Mul(Sine(Hz(190f), Fall(0.07f)), 0.55f),
                    em.Mul(Sine(Hz(330f), Fall(0.04f)), 0.2f));
                var wires = em.Mul(High(white, Bright(1500f, 4500f), 0.15f), Fall(0.16f));

                return (em.Add(body, em.Mul(wires, 0.7f)), Fall(0.16f));
            }

            case Clap:
            {
                // Three quick claps and the room after them.
                var hands = em.Binary(OpCode.Max, em.Binary(OpCode.Max, Burst(0f), Burst(0.012f)), Burst(0.024f));
                var room = em.Mul(em.Mul(Since(0.024f), Decay(em.Sub(age, em.Constant(0.024f)), em.Mul(length, 0.14f))), 0.7f);
                var envelope = em.Binary(OpCode.Max, hands, room);

                return (em.Mul(em.Mul(Band(white, Bright(1000f, 1200f), 0.35f), envelope), 2.5f), envelope);
            }

            case ClosedHat:
            case OpenHat:
            {
                var metal = em.Constant(0f);
                foreach (var hz in Metal)
                    metal = em.Add(metal, Waves.SquareOf(em, em.Phase(domain, Hz(hz), em.Constant(0f))));

                var hiss = em.Add(em.Mul(metal, 0.8f / Metal.Length), em.Mul(white, 0.3f));
                var envelope = Fall(sound == OpenHat ? 0.4f : 0.05f);

                return (em.Mul(em.Mul(High(hiss, Bright(6500f, 3500f), 0.3f), envelope), 2.6f), envelope);
            }

            case PsyKick:
            {
                // Gone before the next sixteenth at any tempo, so a bass rolling on the
                // three after it never lands on its tail. 'decay' fills the body out
                // up to that cut and can only bring the cut earlier.
                var end = em.Mul(em.Mul(sixteenth, em.Binary(OpCode.Min, length, em.Constant(1f))), 0.9f);
                var cut = em.Sub(
                    em.Constant(1f),
                    em.Ternary(OpCode.Smoothstep, em.Mul(end, 0.7f), end, age));
                var envelope = em.Mul(Fall(0.14f), cut);

                var body = Finish.Driven(em, em.Mul(Swept(52f, 330f, 0.014f), em.Mul(envelope, 0.95f)), em.Constant(0.3f));
                var click = em.Mul(
                    em.Mul(white, Decay(age, em.Constant(0.002f))),
                    em.Add(em.Mul(tone, 0.3f), 0.1f));

                return (em.Add(body, em.Mul(click, cut)), envelope);
            }

            case Tom:
            {
                var envelope = Fall(0.4f);
                var click = em.Mul(em.Mul(white, Decay(age, em.Constant(0.003f))), 0.1f);

                return (em.Add(em.Mul(Swept(95f, 80f, 0.08f), em.Mul(envelope, 0.85f)), click), envelope);
            }

            case Rim:
            {
                var envelope = Fall(0.015f);
                var crack = em.Mul(Band(white, Bright(2000f, 1500f), 0.4f), Decay(age, em.Constant(0.006f)));

                return (em.Add(em.Mul(Sine(Hz(1650f), envelope), 0.7f), em.Mul(crack, 0.5f)), envelope);
            }

            case Cowbell:
            {
                var pair = em.Mul(
                    em.Add(
                        Waves.SquareOf(em, em.Phase(domain, Hz(587f), em.Constant(0f))),
                        Waves.SquareOf(em, em.Phase(domain, Hz(845f), em.Constant(0f)))),
                    0.5f);

                // A sharp strike over a longer ring.
                var envelope = em.Add(em.Mul(Decay(age, em.Constant(0.012f)), 0.6f), em.Mul(Fall(0.28f), 0.4f));

                return (em.Mul(em.Mul(Band(pair, Bright(2640f, 1500f), 0.5f), envelope), 0.9f), envelope);
            }

            default:
            {
                var envelope = Fall(0.32f);
                var click = em.Mul(
                    em.Mul(white, Decay(age, em.Constant(0.004f))),
                    em.Add(em.Mul(tone, 0.4f), 0.1f));

                return (em.Add(em.Mul(Swept(48f, 150f, 0.04f), em.Mul(envelope, 0.85f)), click), envelope);
            }
        }

        Slot Hz(float hz) => em.Mul(tune, hz);

        // A fall over 'seconds', stretched by the 'length'.
        Slot Fall(float seconds) => Decay(age, em.Mul(length, seconds));

        Slot Decay(Slot since, Slot seconds) =>
            em.Unary(OpCode.Exp, em.Unary(OpCode.Neg, em.Binary(OpCode.Div, since, seconds)));

        Slot Since(float seconds) => em.Binary(OpCode.Step, em.Constant(seconds), age);

        Slot Burst(float at) => em.Mul(Since(at), Decay(em.Sub(age, em.Constant(at)), em.Constant(0.005f)));

        Slot Sine(Slot hz, Slot level) => em.Mul(em.Unary(OpCode.Sin, em.Mul(em.Mul(hz, age), Tau)), level);

        // A sine that starts 'drop' hertz above 'rest' and falls back over 'seconds': its
        // phase is the integral of that pitch, so it starts at nought on every hit.
        Slot Swept(float rest, float drop, float seconds)
        {
            var fallen = em.Sub(em.Constant(1f), Decay(age, em.Constant(seconds)));
            var cycles = em.Add(em.Mul(Hz(rest), age), em.Mul(Hz(drop), em.Mul(fallen, seconds)));

            return em.Unary(OpCode.Sin, em.Mul(cycles, Tau));
        }

        Slot Bright(float dark, float more) => em.Add(em.Mul(tone, more), dark);

        Slot High(Slot dry, Slot cutoff, float resonance) =>
            NodeCatalog.FilterResponses(em, dry, cutoff, em.Constant(resonance))[2];

        // Scaled to peak at full, as the Easy Synth's band is.
        Slot Band(Slot dry, Slot cutoff, float resonance) =>
            em.Mul(NodeCatalog.FilterResponses(em, dry, cutoff, em.Constant(resonance))[1], 2f - 1.95f * resonance);
    }

    /// <summary>A noise of each sound's own, so two parts of one kit do not hiss in step.</summary>
    private static float SeedOf(string sound) => sound switch
    {
        Snare => 1f,
        Clap => 2f,
        ClosedHat => 3f,
        OpenHat => 4f,
        Tom => 5f,
        Rim => 6f,
        Cowbell => 7f,
        PsyKick => 8f,
        _ => 0f,
    };
}
