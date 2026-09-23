using System.Reflection;
using System.Reflection.Emit;
using ReflectionOpCodes = System.Reflection.Emit.OpCodes;

namespace Flyback.Core.Compile;

/// <summary>
/// Writes one stretch of a program as the IL of one method. The only thing in the
/// IL backend that knows what IL is.
/// </summary>
/// <remarks>
/// Every register the stretch touches is a local. A register it reads before
/// writing was written by an earlier stage and is read from the bank on the way
/// in; a register it writes that a later stage reads, or that is the program's
/// output, is written back to the bank on the way out. Nothing else goes near the
/// bank, which is what lets the JIT keep the arithmetic in the processor's own
/// registers.
/// </remarks>
internal sealed class IlEmitter
{
    private static readonly Type[] Parameters =
    [
        typeof(IlContext),
        typeof(double).MakeByRefType(),
        typeof(double),
        typeof(double),
        typeof(double),
        typeof(double),
        typeof(FeedbackFrame).MakeByRefType(),
        typeof(DelayState),
        typeof(LiveValues),
        typeof(Span<float>),
    ];

    private const short Bank = 1, X = 2, Y = 3, T = 4, Aspect = 5, Feedback = 6, Delays = 7, Live = 8, Planes = 9;

    private static readonly FieldInfo ConstantsField = typeof(IlContext).GetField(nameof(IlContext.Constants))!;
    private static readonly FieldInfo TablesField = typeof(IlContext).GetField(nameof(IlContext.Tables))!;
    private static readonly FieldInfo PicturesField = typeof(IlContext).GetField(nameof(IlContext.Pictures))!;
    private static readonly MethodInfo ConstantMethod = typeof(IlOps).GetMethod(nameof(IlOps.Constant))!;

    private readonly ILGenerator il;
    private readonly Op[] ops;
    private readonly int from;
    private readonly int to;
    private readonly (int From, int To) outputs;
    private readonly Dictionary<int, LocalBuilder> locals = [];

    private IlEmitter(ILGenerator il, Op[] ops, int from, int to, (int From, int To) outputs)
    {
        this.il = il;
        this.ops = ops;
        this.from = from;
        this.to = to;
        this.outputs = outputs;
    }

    /// <summary>
    /// How many ops one method holds. Past a few thousand locals the JIT stops
    /// optimizing a method at all, and every op becomes a call.
    /// </summary>
    internal static int ChunkSize = 256;

    /// <summary>
    /// Ops <paramref name="from"/> to <paramref name="to"/> as methods of at most
    /// <see cref="ChunkSize"/> ops each, to be called in order.
    /// </summary>
    public static DynamicMethod[] EmitChunks(string name, Op[] ops, int from, int to, (int From, int To) outputs)
    {
        var chunks = new List<DynamicMethod>();

        for (var start = from; start < to || chunks.Count == 0; start += ChunkSize)
            chunks.Add(Emit(name, ops, start, Math.Min(to, start + ChunkSize), outputs));

        return [.. chunks];
    }

    /// <summary>
    /// Ops <paramref name="from"/> to <paramref name="to"/> of <paramref name="ops"/>
    /// as one method. The rest of <paramref name="ops"/> is only looked at to learn
    /// which registers another stretch will read.
    /// </summary>
    public static DynamicMethod Emit(string name, Op[] ops, int from, int to, (int From, int To) outputs)
    {
        // Owned by CompiledPatch so it may call what the interpreter calls; the
        // visibility checks are skipped because a dynamic method is not itself a
        // member of anything.
        var method = new DynamicMethod(name, typeof(void), Parameters, typeof(CompiledPatch), skipVisibility: true);

        new IlEmitter(method.GetILGenerator(), ops, from, to, outputs).Run();
        return method;
    }

    private void Run()
    {
        var written = new HashSet<int>();
        var scalarWrites = new List<int>();

        for (var i = from; i < to; i++)
        {
            var op = ops[i];

            foreach (var input in Inputs(op))
            {
                if (written.Contains(input) || locals.ContainsKey(input)) continue;

                Address(input);
                il.Emit(ReflectionOpCodes.Ldind_R8);
                il.Emit(ReflectionOpCodes.Stloc, Local(input));
            }

            var width = OpShape.Outputs(op.Code);
            for (var w = 0; w < width; w++) written.Add(op.Out + w);
            if (width == 1) scalarWrites.Add(op.Out);
        }

        // Counted from the start of the program, as the interpreter counts per walk.
        // Only a whole run is handed state, so a stage's count reaches nothing.
        var line = 0;
        var cell = 0;

        for (var i = 0; i < from; i++)
        {
            if (ops[i].Code is OpCode.Delay or OpCode.Allpass) line++;
            else if (ops[i].Code is OpCode.Phase) cell++;
        }

        for (var i = from; i < to; i++) One(ops[i], ref line, ref cell);

        var readElsewhere = new HashSet<int>();
        for (var i = 0; i < ops.Length; i++)
            if (i < from || i >= to)
                readElsewhere.UnionWith(Inputs(ops[i]));

        foreach (var register in scalarWrites)
        {
            if (!readElsewhere.Contains(register) && (register < outputs.From || register >= outputs.To))
                continue;

            Address(register);
            il.Emit(ReflectionOpCodes.Ldloc, Local(register));
            il.Emit(ReflectionOpCodes.Stind_R8);
        }

        il.Emit(ReflectionOpCodes.Ret);
    }

    private void One(Op op, ref int line, ref int cell)
    {
        switch (op.Code)
        {
            case OpCode.Const:
                il.Emit(ReflectionOpCodes.Ldarg_0);
                il.Emit(ReflectionOpCodes.Ldfld, ConstantsField);
                il.Emit(ReflectionOpCodes.Ldc_I4, op.Out);
                il.Emit(ReflectionOpCodes.Call, ConstantMethod);
                break;

            case OpCode.LoadX: il.Emit(ReflectionOpCodes.Ldarg, X); break;
            case OpCode.LoadY: il.Emit(ReflectionOpCodes.Ldarg, Y); break;
            case OpCode.LoadT: il.Emit(ReflectionOpCodes.Ldarg, T); break;
            case OpCode.LoadAspect: il.Emit(ReflectionOpCodes.Ldarg, Aspect); break;
            case OpCode.Copy: Load(op.A); break;

            case OpCode.LoadLive:
                il.Emit(ReflectionOpCodes.Ldarg, Live);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Call(op.Code);
                break;

            case OpCode.Table:
                il.Emit(ReflectionOpCodes.Ldarg_0);
                il.Emit(ReflectionOpCodes.Ldfld, TablesField);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Load(op.A);
                Call(op.Code);
                break;

            case OpCode.Tap or OpCode.UnitWrite or OpCode.ClockWrite:
                il.Emit(ReflectionOpCodes.Ldarg, Delays);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Load(op.A);
                Call(op.Code);
                break;

            case OpCode.UnitRead:
                il.Emit(ReflectionOpCodes.Ldarg, Delays);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Call(op.Code);
                break;

            case OpCode.PlaneRead:
                il.Emit(ReflectionOpCodes.Ldarg, Delays);
                il.Emit(ReflectionOpCodes.Ldarg, Planes);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Call(op.Code);
                break;

            case OpCode.PlaneWrite:
                il.Emit(ReflectionOpCodes.Ldarg, Delays);
                il.Emit(ReflectionOpCodes.Ldarg, Planes);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Load(op.A);
                Call(op.Code);
                break;

            case OpCode.Delay or OpCode.Allpass:
                il.Emit(ReflectionOpCodes.Ldarg, Delays);
                il.Emit(ReflectionOpCodes.Ldc_I4, line++);
                Load(op.A);
                Load(op.B);
                Load(op.C);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Call(op.Code);
                break;

            case OpCode.Phase:
                il.Emit(ReflectionOpCodes.Ldarg, Delays);
                il.Emit(ReflectionOpCodes.Ldc_I4, cell++);
                Load(op.A);
                Load(op.B);
                Load(op.C);
                Call(op.Code);
                break;

            case OpCode.HsvToRgb:
                Address(0);
                il.Emit(ReflectionOpCodes.Ldc_I4, op.Out);
                Load(op.A);
                Load(op.B);
                Load(op.C);
                Call(op.Code);
                break;

            case OpCode.SampleFeedback:
                Address(0);
                il.Emit(ReflectionOpCodes.Ldc_I4, op.Out);
                il.Emit(ReflectionOpCodes.Ldarg, Feedback);
                Load(op.A);
                Load(op.B);
                Call(op.Code);
                break;

            case OpCode.SamplePicture:
                Address(0);
                il.Emit(ReflectionOpCodes.Ldc_I4, op.Out);
                il.Emit(ReflectionOpCodes.Ldarg_0);
                il.Emit(ReflectionOpCodes.Ldfld, PicturesField);
                il.Emit(ReflectionOpCodes.Ldc_R4, op.K);
                Load(op.A);
                Load(op.B);
                Call(op.Code);
                break;

            default:
                foreach (var input in Inputs(op)) Load(input);
                Call(op.Code);
                break;
        }

        switch (OpShape.Outputs(op.Code))
        {
            case 1:
                il.Emit(ReflectionOpCodes.Stloc, Local(op.Out));
                break;

            // Written into the bank by the helper, and taken back into locals so
            // the ops after it read them the way they read everything else.
            case 3:
                for (var w = 0; w < 3; w++)
                {
                    Address(op.Out + w);
                    il.Emit(ReflectionOpCodes.Ldind_R8);
                    il.Emit(ReflectionOpCodes.Stloc, Local(op.Out + w));
                }

                break;
        }
    }

    private void Call(OpCode code) =>
        il.Emit(
            ReflectionOpCodes.Call,
            IlOps.For(code) ?? throw new NotSupportedException($"The IL backend has no lowering for {code}."));

    private void Load(int register) => il.Emit(ReflectionOpCodes.Ldloc, Local(register));

    private LocalBuilder Local(int register)
    {
        if (!locals.TryGetValue(register, out var local))
            locals[register] = local = il.DeclareLocal(typeof(double));

        return local;
    }

    /// <summary>Pushes <c>ref bank[register]</c>.</summary>
    private void Address(int register)
    {
        il.Emit(ReflectionOpCodes.Ldarg, Bank);
        if (register == 0) return;

        il.Emit(ReflectionOpCodes.Ldc_I4, register * sizeof(double));
        il.Emit(ReflectionOpCodes.Conv_I);
        il.Emit(ReflectionOpCodes.Add);
    }

    private static IEnumerable<int> Inputs(Op op)
    {
        var count = OpShape.Inputs(op.Code);
        if (count > 0) yield return op.A;
        if (count > 1) yield return op.B;
        if (count > 2) yield return op.C;
    }
}
