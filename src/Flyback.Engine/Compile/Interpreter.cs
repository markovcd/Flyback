using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flyback.Core.Compile;
using static Flyback.Engine.Compile.Arithmetic;

namespace Flyback.Engine.Compile;

/// <summary>
/// Runs a <see cref="CompiledPatch"/>'s ops one at a time: the reference every
/// other backend is held to (ADR-0035).
/// </summary>
internal static class Interpreter
{
    /// <summary>
    /// Walks <paramref name="ops"/> from <paramref name="from"/> to
    /// <paramref name="to"/>, so the staged path can run a third of the program
    /// without a second copy of the switch.
    /// </summary>
    /// <remarks>
    /// Only a whole run may pass <paramref name="delays"/>: which line or cell a
    /// stateful op uses is counted from the start of the program. A staged run
    /// passes none, which is also what lets its ops be reordered at all.
    /// <para>
    /// <paramref name="planes"/> is the exception, and can be passed to either,
    /// because a plane is named by its op rather than counted: a staged run
    /// carries every plane op in the stage a pixel pays for — see
    /// <see cref="FramePlan"/>.
    /// </para>
    /// </remarks>
    internal static void Run(
        CompiledPatch program,
        Op[] ops,
        int from,
        int to,
        double x,
        double y,
        double t,
        Span<double> registers,
        in FeedbackFrame feedback,
        DelayState? delays,
        double aspect,
        LiveValues? live,
        Span<float> planes)
    {
        if (registers.Length < program.RegisterCount)
            throw new ArgumentException(
                $"Register bank holds {registers.Length}, the program needs {program.RegisterCount}.",
                nameof(registers));

        // The one place the register file is touched without a bounds check. Every
        // index below was checked against RegisterCount when the program was made,
        // and the guard above is the other half of that. Checking per access costs
        // four compares on an Add, which at thirty ops a pixel is most of what
        // this loop does.
        ref var bank = ref MemoryMarshal.GetReference(registers);

        var pictures = program.PictureArray;
        var tables = program.TableArray;

        // Which line or cell an op uses is its position among the ops of its
        // kind, so each kind is counted on its own.
        var line = 0;
        var cell = 0;

        for (var index = from; index < to; index++)
        {
            ref readonly var op = ref ops[index];

            switch (op.Code)
            {
                case OpCode.Const: Reg(ref bank, op.Out) = op.K; break;
                case OpCode.LoadX: Reg(ref bank, op.Out) = x; break;
                case OpCode.LoadY: Reg(ref bank, op.Out) = y; break;
                case OpCode.LoadT: Reg(ref bank, op.Out) = t; break;
                case OpCode.LoadAspect: Reg(ref bank, op.Out) = aspect; break;
                case OpCode.LoadLive: Reg(ref bank, op.Out) = live?.At((int)op.K) ?? 0d; break;
                case OpCode.Copy: Reg(ref bank, op.Out) = Reg(ref bank, op.A); break;

                case OpCode.Neg: Reg(ref bank, op.Out) = -Reg(ref bank, op.A); break;
                case OpCode.Abs: Reg(ref bank, op.Out) = Math.Abs(Reg(ref bank, op.A)); break;
                case OpCode.Sin: Reg(ref bank, op.Out) = Math.Sin(Reg(ref bank, op.A)); break;
                case OpCode.Cos: Reg(ref bank, op.Out) = Math.Cos(Reg(ref bank, op.A)); break;
                case OpCode.Tan: Reg(ref bank, op.Out) = Guard(Math.Tan(Reg(ref bank, op.A))); break;
                case OpCode.Sqrt:
                {
                    var a = Reg(ref bank, op.A);
                    Reg(ref bank, op.Out) = a <= 0d ? 0d : Math.Sqrt(a);
                    break;
                }

                case OpCode.Floor: Reg(ref bank, op.Out) = Math.Floor(Reg(ref bank, op.A)); break;
                case OpCode.Ceil: Reg(ref bank, op.Out) = Math.Ceiling(Reg(ref bank, op.A)); break;
                case OpCode.Fract: Reg(ref bank, op.Out) = Fract(Reg(ref bank, op.A)); break;
                case OpCode.Sign: Reg(ref bank, op.Out) = Signum(Reg(ref bank, op.A)); break;
                case OpCode.Exp: Reg(ref bank, op.Out) = Guard(Math.Exp(Reg(ref bank, op.A))); break;
                case OpCode.Log:
                {
                    var a = Reg(ref bank, op.A);
                    Reg(ref bank, op.Out) = a <= 0d ? 0d : Math.Log(a);
                    break;
                }

                case OpCode.Add: Reg(ref bank, op.Out) = Reg(ref bank, op.A) + Reg(ref bank, op.B); break;
                case OpCode.Sub: Reg(ref bank, op.Out) = Reg(ref bank, op.A) - Reg(ref bank, op.B); break;
                case OpCode.Mul: Reg(ref bank, op.Out) = Reg(ref bank, op.A) * Reg(ref bank, op.B); break;
                case OpCode.Div: Reg(ref bank, op.Out) = Divide(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Mod: Reg(ref bank, op.Out) = Modulo(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Pow: Reg(ref bank, op.Out) = Guard(Math.Pow(Reg(ref bank, op.A), Reg(ref bank, op.B))); break;
                case OpCode.Min: Reg(ref bank, op.Out) = Math.Min(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Max: Reg(ref bank, op.Out) = Math.Max(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Atan2: Reg(ref bank, op.Out) = Math.Atan2(Reg(ref bank, op.A), Reg(ref bank, op.B)); break;
                case OpCode.Step: Reg(ref bank, op.Out) = Reg(ref bank, op.B) < Reg(ref bank, op.A) ? 0d : 1d; break;
                case OpCode.Hypot:
                {
                    double a = Reg(ref bank, op.A), b = Reg(ref bank, op.B);
                    Reg(ref bank, op.Out) = Math.Sqrt(a * a + b * b);
                    break;
                }

                case OpCode.Clamp:
                {
                    double a = Reg(ref bank, op.A), b = Reg(ref bank, op.B), c = Reg(ref bank, op.C);
                    Reg(ref bank, op.Out) = Math.Clamp(a, b, Math.Max(b, c));
                    break;
                }

                case OpCode.Mix:
                {
                    double a = Reg(ref bank, op.A), b = Reg(ref bank, op.B), f = Reg(ref bank, op.C);
                    Reg(ref bank, op.Out) = a + (b - a) * f;
                    break;
                }

                case OpCode.Smoothstep:
                    Reg(ref bank, op.Out) = Smoothstep(Reg(ref bank, op.A), Reg(ref bank, op.B), Reg(ref bank, op.C));
                    break;

                case OpCode.Noise3:
                    Reg(ref bank, op.Out) = Noise.Value3(Reg(ref bank, op.A), Reg(ref bank, op.B), Reg(ref bank, op.C));
                    break;

                case OpCode.HsvToRgb:
                    HsvToRgb(
                        Reg(ref bank, op.A),
                        Reg(ref bank, op.B),
                        Reg(ref bank, op.C),
                        Triple(ref bank, op.Out));
                    break;

                case OpCode.SampleFeedback:
                    Sample(feedback, Reg(ref bank, op.A), Reg(ref bank, op.B), Triple(ref bank, op.Out));
                    break;

                case OpCode.SamplePicture:
                {
                    var picture = (int)op.K;
                    var rgb = Triple(ref bank, op.Out);

                    // Black where the program carries no pictures, which is
                    // every audio program and any video one whose file was not
                    // there — the same answer a Table gives silence for.
                    if ((uint)picture < (uint)pictures.Length)
                        pictures[picture].At(Reg(ref bank, op.A), Reg(ref bank, op.B), rgb);
                    else
                        rgb[0] = rgb[1] = rgb[2] = 0d;

                    break;
                }

                case OpCode.Tap:
                    delays?.Tap((int)op.K, Reg(ref bank, op.A));
                    break;

                case OpCode.Table:
                {
                    var clip = (int)op.K;

                    // Silence where the program carries no clips, which is every
                    // video program and any audio one whose file was not there.
                    Reg(ref bank, op.Out) = (uint)clip < (uint)tables.Length
                        ? tables[clip].At(Reg(ref bank, op.A))
                        : 0d;
                    break;
                }

                case OpCode.Delay:
                {
                    var slot = line++;
                    if (delays is null) { Reg(ref bank, op.Out) = Reg(ref bank, op.A); break; }

                    // Read before write, so the shortest possible delay is one
                    // evaluation. A zero-sample loop would be algebraic, and
                    // there would be nothing for it to mean.
                    var heard = delays.Read(slot, Reg(ref bank, op.C), op.K);
                    delays.Write(slot, Reg(ref bank, op.A) + Feedback(Reg(ref bank, op.B)) * heard);
                    Reg(ref bank, op.Out) = heard;
                    break;
                }

                case OpCode.Allpass:
                {
                    var slot = line++;
                    if (delays is null) { Reg(ref bank, op.Out) = Reg(ref bank, op.A); break; }

                    var heard = delays.Read(slot, Reg(ref bank, op.C), op.K);
                    var gain = Feedback(Reg(ref bank, op.B));
                    var stored = Reg(ref bank, op.A) + gain * heard;

                    delays.Write(slot, stored);
                    Reg(ref bank, op.Out) = heard - gain * stored;
                    break;
                }

                // The two halves of a cycle. Without state a read is zero and a
                // write goes nowhere, so a loop drawn on the video path is simply
                // open: pixels are evaluated in parallel, and there is no
                // "previous evaluation" for one to mean.
                case OpCode.UnitRead:
                    Reg(ref bank, op.Out) = delays?.ReadUnit((int)op.K) ?? 0d;
                    break;

                case OpCode.UnitWrite:
                    delays?.WriteUnit((int)op.K, Reg(ref bank, op.A));
                    break;

                case OpCode.ClockWrite:
                    delays?.WriteClock((int)op.K, Reg(ref bank, op.A));
                    break;

                // The same pair, for a cycle both sinks can carry. The ear's
                // previous evaluation is the sample before and lives in the state
                // beside the delay lines; the eye's is this pixel in the frame
                // before, and is the one number of the plane that belongs to it.
                // Handed neither, a loop reads zero and stays open.
                case OpCode.PlaneRead:
                {
                    var slot = (int)op.K;

                    Reg(ref bank, op.Out) = delays is not null
                        ? delays.ReadPlane(slot)
                        : (uint)slot < (uint)planes.Length ? planes[slot] : 0d;
                    break;
                }

                case OpCode.PlaneWrite:
                {
                    var slot = (int)op.K;
                    var value = Reg(ref bank, op.A);

                    if (delays is not null) delays.WritePlane(slot, value);
                    else if ((uint)slot < (uint)planes.Length) planes[slot] = Bounded(value);

                    break;
                }

                case OpCode.Phase:
                {
                    var slot = cell++;
                    double input = Reg(ref bank, op.A), frequency = Reg(ref bank, op.B);

                    // Without state there is no previous evaluation to step from
                    // — a picture's pixels are one evaluation each, in whatever
                    // order the rows happen to run — so this is the multiply the
                    // accumulator replaces, and over a still frame the two agree.
                    Reg(ref bank, op.Out) = delays is null
                        ? input * frequency + Reg(ref bank, op.C)
                        : delays.Advance(slot, input, frequency) + Reg(ref bank, op.C);
                    break;
                }
            }
        }
    }

    /// <summary>Register <paramref name="index"/> of a bank <see cref="CompiledPatch"/> has already vouched for.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref double Reg(ref double bank, int index) => ref Unsafe.Add(ref bank, index);

    /// <summary>The three consecutive registers a color-width op writes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Span<double> Triple(ref double bank, int first) =>
        MemoryMarshal.CreateSpan(ref Reg(ref bank, first), 3);
}
