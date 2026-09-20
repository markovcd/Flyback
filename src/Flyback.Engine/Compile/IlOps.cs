using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flyback.Core.Compile;

/// <summary>
/// One method per op, holding what that op's case in
/// <see cref="CompiledPatch"/>'s switch does, for the IL backend's methods to call.
/// </summary>
/// <remarks>
/// Every guard is the interpreter's own helper, called rather than written again,
/// so a zero divisor or a non-finite power is answered in one place. What is
/// written here is only the glue a case puts round them, and it is kept a line
/// for a line with the case so the two can be read side by side. The tests that
/// run both over every preset are what notice the glue drifting.
/// <para>
/// Each is small enough for the JIT to inline into the emitted method, which is
/// what removes the dispatch: the emitted code is a run of these, one after another,
/// with the registers in locals between them.
/// </para>
/// </remarks>
internal static class IlOps
{
    private const MethodImplOptions Inline = MethodImplOptions.AggressiveInlining;

    [MethodImpl(Inline)] public static double Neg(double a) => -a;
    [MethodImpl(Inline)] public static double Abs(double a) => Math.Abs(a);
    [MethodImpl(Inline)] public static double Sin(double a) => Math.Sin(a);
    [MethodImpl(Inline)] public static double Cos(double a) => Math.Cos(a);
    [MethodImpl(Inline)] public static double Tan(double a) => CompiledPatch.Guard(Math.Tan(a));
    [MethodImpl(Inline)] public static double Sqrt(double a) => a <= 0d ? 0d : Math.Sqrt(a);
    [MethodImpl(Inline)] public static double Floor(double a) => Math.Floor(a);
    [MethodImpl(Inline)] public static double Ceil(double a) => Math.Ceiling(a);
    [MethodImpl(Inline)] public static double Fract(double a) => CompiledPatch.Fract(a);
    [MethodImpl(Inline)] public static double Sign(double a) => CompiledPatch.Signum(a);
    [MethodImpl(Inline)] public static double Exp(double a) => CompiledPatch.Guard(Math.Exp(a));
    [MethodImpl(Inline)] public static double Log(double a) => a <= 0d ? 0d : Math.Log(a);

    [MethodImpl(Inline)] public static double Add(double a, double b) => a + b;
    [MethodImpl(Inline)] public static double Sub(double a, double b) => a - b;
    [MethodImpl(Inline)] public static double Mul(double a, double b) => a * b;
    [MethodImpl(Inline)] public static double Div(double a, double b) => CompiledPatch.Divide(a, b);
    [MethodImpl(Inline)] public static double Mod(double a, double b) => CompiledPatch.Modulo(a, b);
    [MethodImpl(Inline)] public static double Pow(double a, double b) => CompiledPatch.Guard(Math.Pow(a, b));
    [MethodImpl(Inline)] public static double Min(double a, double b) => Math.Min(a, b);
    [MethodImpl(Inline)] public static double Max(double a, double b) => Math.Max(a, b);
    [MethodImpl(Inline)] public static double Atan2(double a, double b) => Math.Atan2(a, b);
    [MethodImpl(Inline)] public static double Step(double a, double b) => b < a ? 0d : 1d;
    [MethodImpl(Inline)] public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    [MethodImpl(Inline)] public static double Clamp(double a, double b, double c) => Math.Clamp(a, b, Math.Max(b, c));
    [MethodImpl(Inline)] public static double Mix(double a, double b, double f) => a + (b - a) * f;
    [MethodImpl(Inline)] public static double Smoothstep(double a, double b, double c) => CompiledPatch.Smoothstep(a, b, c);
    [MethodImpl(Inline)] public static double Noise3(double a, double b, double c) => Noise.Value3(a, b, c);

    [MethodImpl(Inline)] public static double LoadLive(LiveValues? live, float k) => live?.At((int)k) ?? 0d;

    [MethodImpl(Inline)]
    public static double Table(LoadedSample[] tables, float k, double a)
    {
        var clip = (int)k;
        return (uint)clip < (uint)tables.Length ? tables[clip].At(a) : 0d;
    }

    [MethodImpl(Inline)] public static void Tap(DelayState? delays, float k, double a) => delays?.Tap((int)k, a);
    [MethodImpl(Inline)] public static double UnitRead(DelayState? delays, float k) => delays?.ReadUnit((int)k) ?? 0d;
    [MethodImpl(Inline)] public static void UnitWrite(DelayState? delays, float k, double a) => delays?.WriteUnit((int)k, a);
    [MethodImpl(Inline)] public static void ClockWrite(DelayState? delays, float k, double a) => delays?.WriteClock((int)k, a);

    [MethodImpl(Inline)]
    public static double PlaneRead(DelayState? delays, Span<float> planes, float k)
    {
        var slot = (int)k;

        return delays is not null
            ? delays.ReadPlane(slot)
            : (uint)slot < (uint)planes.Length ? planes[slot] : 0d;
    }

    [MethodImpl(Inline)]
    public static void PlaneWrite(DelayState? delays, Span<float> planes, float k, double value)
    {
        var slot = (int)k;

        if (delays is not null) delays.WritePlane(slot, value);
        else if ((uint)slot < (uint)planes.Length) planes[slot] = CompiledPatch.Bounded(value);
    }

    /// <summary><paramref name="slot"/> is counted when the IL is emitted, where the interpreter counts it as it walks.</summary>
    [MethodImpl(Inline)]
    public static double Delay(DelayState? delays, int slot, double a, double b, double c, float k)
    {
        if (delays is null) return a;

        var heard = delays.Read(slot, c, k);
        delays.Write(slot, a + CompiledPatch.Feedback(b) * heard);
        return heard;
    }

    [MethodImpl(Inline)]
    public static double Allpass(DelayState? delays, int slot, double a, double b, double c, float k)
    {
        if (delays is null) return a;

        var heard = delays.Read(slot, c, k);
        var gain = CompiledPatch.Feedback(b);
        var stored = a + gain * heard;

        delays.Write(slot, stored);
        return heard - gain * stored;
    }

    [MethodImpl(Inline)]
    public static double Phase(DelayState? delays, int cell, double input, double frequency, double offset) =>
        delays is null
            ? input * frequency + offset
            : delays.Advance(cell, input, frequency) + offset;

    [MethodImpl(Inline)]
    public static void HsvToRgb(ref double bank, int first, double h, double s, double v) =>
        CompiledPatch.HsvToRgb(h, s, v, Triple(ref bank, first));

    [MethodImpl(Inline)]
    public static void SampleFeedback(ref double bank, int first, ref FeedbackFrame feedback, double u, double v) =>
        CompiledPatch.Sample(feedback, u, v, Triple(ref bank, first));

    [MethodImpl(Inline)]
    public static void SamplePicture(ref double bank, int first, LoadedImage[] pictures, float k, double a, double b)
    {
        var picture = (int)k;
        var rgb = Triple(ref bank, first);

        if ((uint)picture < (uint)pictures.Length)
            pictures[picture].At(a, b, rgb);
        else
            rgb[0] = rgb[1] = rgb[2] = 0d;
    }

    /// <summary>
    /// The register a constant is read from, out of an array the constructor sized
    /// to the whole bank — so, like the bank, it is read without a bounds check.
    /// </summary>
    [MethodImpl(Inline)]
    public static double Constant(double[] constants, int register) =>
        Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(constants), register);

    /// <summary>What the emitter calls for <paramref name="code"/>, or null for an op it writes out itself.</summary>
    public static MethodInfo? For(OpCode code) =>
        typeof(IlOps).GetMethod(code.ToString(), BindingFlags.Public | BindingFlags.Static);

    [MethodImpl(Inline)]
    private static Span<double> Triple(ref double bank, int first) =>
        MemoryMarshal.CreateSpan(ref Unsafe.Add(ref bank, first), 3);
}
