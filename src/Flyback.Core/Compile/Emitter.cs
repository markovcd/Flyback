namespace Flyback.Core.Compile;

/// <summary>
/// Builds the flat op list for a patch. Every value gets a freshly allocated
/// register, so the program is in SSA form and no op ever aliases its inputs.
/// </summary>
public sealed class Emitter
{
    private readonly List<Op> _ops = [];
    private readonly Dictionary<float, Slot> _constants = [];
    private readonly Dictionary<OpCode, Slot> _loads = [];
    private int _registerCount;

    public int RegisterCount => _registerCount;

    public Op[] ToProgram() => [.. _ops];

    public int Allocate(int count)
    {
        var first = _registerCount;
        _registerCount += count;
        return first;
    }

    public void Add(Op op) => _ops.Add(op);

    /// <summary>A scalar literal. Repeated values share a register.</summary>
    public Slot Constant(float value)
    {
        if (_constants.TryGetValue(value, out var existing)) return existing;

        var slot = Slot.Scalar(Allocate(1));
        Add(new Op(OpCode.Const, slot.Base, k: value));
        _constants[value] = slot;
        return slot;
    }

    /// <summary>A per-pixel input (x, y or time). Emitted once and reused.</summary>
    public Slot Load(OpCode code)
    {
        if (_loads.TryGetValue(code, out var existing)) return existing;

        var slot = Slot.Scalar(Allocate(1));
        Add(new Op(code, slot.Base));
        _loads[code] = slot;
        return slot;
    }

    public Slot Unary(OpCode code, Slot a)
    {
        var first = Allocate(a.Width);
        for (var i = 0; i < a.Width; i++)
            Add(new Op(code, first + i, a.Component(i)));
        return new Slot(first, a.Width);
    }

    public Slot Binary(OpCode code, Slot a, Slot b)
    {
        var width = Math.Max(a.Width, b.Width);
        var first = Allocate(width);
        for (var i = 0; i < width; i++)
            Add(new Op(code, first + i, a.Component(i), b.Component(i)));
        return new Slot(first, width);
    }

    public Slot Ternary(OpCode code, Slot a, Slot b, Slot c)
    {
        var width = Math.Max(a.Width, Math.Max(b.Width, c.Width));
        var first = Allocate(width);
        for (var i = 0; i < width; i++)
            Add(new Op(code, first + i, a.Component(i), b.Component(i), c.Component(i)));
        return new Slot(first, width);
    }

    /// <summary>An op that writes three consecutive registers at once.</summary>
    public Slot Triple(OpCode code, Slot a, Slot b, Slot c = default)
    {
        var first = Allocate(3);
        Add(new Op(code, first, a.Component(0), b.Component(0), c.Width == 0 ? -1 : c.Component(0)));
        return Slot.Colour(first);
    }

    // --- convenience wrappers used all over the node catalogue ---

    public Slot Add(Slot a, Slot b) => Binary(OpCode.Add, a, b);

    public Slot Sub(Slot a, Slot b) => Binary(OpCode.Sub, a, b);

    public Slot Mul(Slot a, Slot b) => Binary(OpCode.Mul, a, b);

    public Slot Mul(Slot a, float b) => Binary(OpCode.Mul, a, Constant(b));

    public Slot Add(Slot a, float b) => Binary(OpCode.Add, a, Constant(b));

    /// <summary>Widens a scalar to three components; colours pass through.</summary>
    public Slot ToColour(Slot value)
    {
        if (value.Width == 3) return value;

        var first = Allocate(3);
        for (var i = 0; i < 3; i++)
            Add(new Op(OpCode.Copy, first + i, value.Base));
        return Slot.Colour(first);
    }

    /// <summary>Narrows a colour to a scalar using broadcast luma weights.</summary>
    public Slot ToScalar(Slot value)
    {
        if (value.Width == 1) return value;

        var r = Mul(Slot.Scalar(value.Base + 0), 0.2126f);
        var g = Mul(Slot.Scalar(value.Base + 1), 0.7152f);
        var b = Mul(Slot.Scalar(value.Base + 2), 0.0722f);
        return Add(Add(r, g), b);
    }

    /// <summary>Packs three scalars into a colour occupying consecutive registers.</summary>
    public Slot Combine(Slot r, Slot g, Slot b)
    {
        var first = Allocate(3);
        Add(new Op(OpCode.Copy, first + 0, r.Component(0)));
        Add(new Op(OpCode.Copy, first + 1, g.Component(0)));
        Add(new Op(OpCode.Copy, first + 2, b.Component(0)));
        return Slot.Colour(first);
    }

    /// <summary>Coerces a value to the width a port expects.</summary>
    public Slot Coerce(Slot value, int width) => width == 3 ? ToColour(value) : ToScalar(value);
}
