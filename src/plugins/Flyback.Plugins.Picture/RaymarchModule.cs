using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Picture;

/// <summary>
/// A 3D scene seen through a camera flying into it, drawn by marching a ray from
/// each pixel along a distance field until it touches a surface.
/// </summary>
/// <remarks>
/// The machine has no loops, so every step of the march is the scene's distance
/// function written out again: the step count is a setting, and the largest
/// counts make the longest programs Flyback builds. A ray that has landed is
/// frozen rather than stopped, as an escaped point is in Fractals, so every
/// pixel runs every step and no value goes unbounded.
/// <para>
/// The scene is built in the camera's frame: the camera sits at the origin
/// looking down +z, 'travel' slides the scene towards it, and 'twist' turns each
/// slice of the scene about the camera's axis, so the camera never ends up
/// inside a shape.
/// </para>
/// </remarks>
internal static class RaymarchModule
{
    public const string TypeId = "flyback.picture.raymarch";

    public const string StateKey = "march";
    public const string SceneKey = "scene";
    public const string StepsKey = "steps";

    public const string Lattice = "lattice";
    public const string Tunnel = "tunnel";
    public const string Gyroid = "gyroid";

    public const int TravelPort = 2;
    public const int RollPort = 3;
    public const int TwistPort = 4;
    public const int PulsePort = 5;
    public const int MorphPort = 6;
    public const int SpacingPort = 7;
    public const int GlowPort = 8;
    public const int FogPort = 9;
    public const int HuePort = 10;

    /// <summary>How far a ray is followed before it counts as sky.</summary>
    public const float Far = 40f;

    /// <summary>Where a ray starts, clear of the lens.</summary>
    private const float Near = 0.05f;

    /// <summary>How wide the lens is: x and y at the screen's edge over the distance ahead.</summary>
    private const float Lens = 0.9f;

    /// <summary>
    /// The share of the distance each step takes. Twisting and wobbling a shape
    /// make its distance overstate the gap, so a ray takes less than all of it.
    /// </summary>
    private const float Slack = 0.7f;

    /// <summary>A ray has landed once the gap is under this share of how far it has come.</summary>
    private const float Touch = 0.0015f;

    /// <summary>How near a ray that missed must have passed a surface to glow.</summary>
    private const float GlowReach = 0.6f;

    /// <summary>How far apart the normal's four samples are.</summary>
    private const float Nudge = 0.004f;

    private const float Tau = 6.283185307179586f;

    private const string FallbackSteps = "64";

    private static readonly int[] Counts = [16, 32, 64, 128, 256];

    private static readonly string[] Scenes = [Lattice, Tunnel, Gyroid];

    /// <summary>The scene and the step count, the two settings that decide which ops are built.</summary>
    public static NodeExtra Extra { get; } = new SettingsExtra(StateKey,
    [
        new ExtraField.Choice(
            SceneKey, "scene",
            [
                new ChoiceOption(Lattice, "lattice: shapes repeated without end"),
                new ChoiceOption(Tunnel, "tunnel: a ribbed tube to fly down"),
                new ChoiceOption(Gyroid, "gyroid: a curved sheet, like coral"),
            ],
            Lattice) { Help = "What the camera flies through." },
        new ExtraField.Choice(
            StepsKey, "steps",
            [.. Counts.Select(n => new ChoiceOption(n.ToString(), $"{n} steps"))],
            FallbackSteps)
        {
            Help = "How many times each ray moves on. More reaches further and sharpens edges, and every step is the scene built again.",
        },
    ]);

    public static NodeDef Definition { get; } = new(
        TypeId, "Raymarch", ModuleCategories.Patterns,
        [
            new PortSpec("x", NormalledTo: NodeCatalog.Across) { Standard = true },
            new PortSpec("y", NormalledTo: NodeCatalog.Down) { Standard = true },
            new PortSpec("travel", PortKind.Scalar, 0f, 0f, 100f)
            {
                Help = "How far the camera has flown. A clock on it is a steady flight, and a kick on it a lurch.",
            },
            new PortSpec("roll", PortKind.Scalar, 0f, 0f, 1f) { Help = "Turns the camera round its view, in turns." },
            new PortSpec("twist", PortKind.Scalar, 0.1f, -1f, 1f) { Help = "Corkscrews the scene ahead, more the further away." },
            new PortSpec("pulse", PortKind.Scalar, 0f, 0f, 1f) { Help = "Swells every shape. An envelope on it is a beat you can see." },
            new PortSpec("morph", PortKind.Scalar, 0f, 0f, 1f)
            {
                Help = "Reshapes the scene: spheres to boxes, a round tunnel to a square one, a gyroid to its cousin.",
            },
            new PortSpec("spacing", PortKind.Scalar, 3f, 1f, 8f) { Help = "How far apart the repeats are." },
            new PortSpec("glow", PortKind.Scalar, 0.5f, 0f, 1f) { Help = "A halo where rays only just missed a surface." },
            new PortSpec("fog", PortKind.Scalar, 0.5f, 0f, 1f) { Help = "How fast the distance fades into the dark." },
            new PortSpec("hue", PortKind.Scalar, 0f, 0f, 1f) { Help = "Turns the colors. Each shape is shaded a little off it." },
        ],
        [
            new PortSpec("color", PortKind.Color) { Help = "The lit scene, fogged, over a dark sky." },
            new PortSpec("depth", PortKind.Scalar, 0f, 0f, 1f) { Help = "How far each ray went, 0 at the lens to 1 for the sky." },
            new PortSpec("hit", PortKind.Scalar, 0f, 0f, 1f) { Help = "One where the ray found a surface, nought for the sky." },
            new PortSpec("glow", PortKind.Scalar, 0f, 0f, 1f) { Help = "How closely a ray that missed passed a surface: a halo round every edge." },
            new PortSpec("shade", PortKind.Scalar, 0f, 0f, 1f) { Help = "The light on the surface alone, fogged. For a Palette of your own." },
        ],
        Emit,
        "A 3D scene the camera flies through, drawn by marching a ray from each pixel. "
        + "The scene and the step count are set on the node; each step costs the scene's whole distance function.")
    {
        Words = "3d, ray marching, sdf, tunnel, flythrough",
        Extras = [Extra],
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Patterns))
        {
            Glyph = "M15,9 A5.5,5.5 0 1 1 15,20 A5.5,5.5 0 1 1 15,9 M3,3 L5.5,5.5 M7.5,7.5 L10,10",
        },
    };

    /// <summary>The step count this instance carries, held to one the module offers.</summary>
    public static int Steps(EmitContext node) =>
        int.TryParse(node.Extra<ExtraState>(StateKey)?.Chosen(StepsKey), out var count)
            ? Counts.MinBy(n => Math.Abs(n - count))
            : int.Parse(FallbackSteps);

    /// <summary>The scene this instance carries, the lattice for anything it does not know.</summary>
    public static string Scene(EmitContext node) =>
        node.Extra<ExtraState>(StateKey)?.Chosen(SceneKey) is { } scene && Scenes.Contains(scene) ? scene : Lattice;

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var shape = new Shape(em, Scene(node), node);
        var steps = Steps(node);
        var travel = node[TravelPort];

        // The ray through this pixel, turned by 'roll' and made one long.
        var roll = em.Mul(node[RollPort], Tau);
        var (cos, sin) = (em.Unary(OpCode.Cos, roll), em.Unary(OpCode.Sin, roll));
        var sx = em.Mul(em.Sub(em.Mul(node[0], cos), em.Mul(node[1], sin)), Lens);
        var sy = em.Mul(em.Add(em.Mul(node[0], sin), em.Mul(node[1], cos)), Lens);
        var length = em.Binary(OpCode.Hypot, em.Binary(OpCode.Hypot, sx, sy), one);
        var (dx, dy, dz) = (em.Binary(OpCode.Div, sx, length), em.Binary(OpCode.Div, sy, length), em.Binary(OpCode.Div, one, length));

        var t = em.Constant(Near);
        var landed = zero;
        var closest = em.Constant(Far);

        for (var i = 0; i < steps; i++)
        {
            var gap = shape.Distance(em.Mul(dx, t), em.Mul(dy, t), em.Add(travel, em.Mul(dz, t)));

            landed = em.Binary(OpCode.Max, landed, em.Binary(OpCode.Step, gap, em.Mul(t, Touch)));
            var moving = em.Sub(one, landed);

            closest = em.Binary(OpCode.Min, closest, em.Add(gap, em.Mul(landed, Far)));

            t = em.Add(t, em.Mul(em.Mul(gap, Slack), moving));
        }

        // A ray that ran out of steps short of a surface is sky, wherever it got to.
        t = em.Binary(OpCode.Min, t, em.Constant(Far));
        var hit = em.Mul(landed, em.Binary(OpCode.Step, t, em.Constant(Far - 0.01f)));

        var (px, py, pz) = (em.Mul(dx, t), em.Mul(dy, t), em.Add(travel, em.Mul(dz, t)));
        var (nx, ny, nz) = Normal(em, shape, px, py, pz);

        // A light just above and right of the lens, and a rim where the surface turns away.
        var facing = em.Binary(OpCode.Max, em.Unary(OpCode.Neg, Dot(em, nx, ny, nz, dx, dy, dz)), zero);
        var lit = em.Binary(OpCode.Max, Dot(em, nx, ny, nz, em.Constant(0.33f), em.Constant(0.5f), em.Constant(-0.8f)), zero);
        var edge = em.Sub(one, facing);
        var shade = em.Ternary(OpCode.Clamp, em.Add(em.Add(em.Mul(lit, 0.8f), 0.2f), em.Mul(em.Mul(edge, edge), 0.45f)), zero, one);

        var fog = em.Mul(em.Unary(OpCode.Exp, em.Mul(em.Mul(t, node[FogPort]), -0.25f)), hit);
        var near = em.Ternary(OpCode.Clamp, em.Sub(one, em.Mul(closest, 1f / GlowReach)), zero, one);
        var glow = em.Mul(em.Mul(near, near), em.Sub(one, hit));
        var hue = node[HuePort];

        var surface = Palette(em, em.Add(hue, shape.Tone(px, py, pz)));
        var sky = em.Mul(Palette(em, em.Add(hue, 0.5f)), 0.05f);
        var halo = em.Mul(Palette(em, em.Add(hue, 0.18f)), em.Mul(glow, node[GlowPort]));

        var color = em.Ternary(OpCode.Mix, sky, em.Mul(surface, shade), fog);
        color = em.Ternary(OpCode.Clamp, em.Add(color, halo), zero, one);

        var depth = em.Ternary(OpCode.Mix, one, em.Mul(t, 1f / Far), hit);

        return [color, depth, hit, glow, em.Mul(shade, fog)];
    }

    /// <summary>The surface's facing at a point: the distance sampled at four corners of a tiny tetrahedron.</summary>
    private static (Slot X, Slot Y, Slot Z) Normal(Emitter em, Shape shape, Slot x, Slot y, Slot z)
    {
        ReadOnlySpan<(float X, float Y, float Z)> corners = [(1, -1, -1), (-1, -1, 1), (-1, 1, -1), (1, 1, 1)];

        Slot nx = em.Constant(0f), ny = nx, nz = nx;

        foreach (var (cx, cy, cz) in corners)
        {
            var d = shape.Distance(em.Add(x, cx * Nudge), em.Add(y, cy * Nudge), em.Add(z, cz * Nudge));

            nx = em.Add(nx, em.Mul(d, cx));
            ny = em.Add(ny, em.Mul(d, cy));
            nz = em.Add(nz, em.Mul(d, cz));
        }

        var length = em.Binary(OpCode.Max, em.Binary(OpCode.Hypot, em.Binary(OpCode.Hypot, nx, ny), nz), em.Constant(1e-9f));

        return (em.Binary(OpCode.Div, nx, length), em.Binary(OpCode.Div, ny, length), em.Binary(OpCode.Div, nz, length));
    }

    private static Slot Dot(Emitter em, Slot ax, Slot ay, Slot az, Slot bx, Slot by, Slot bz) =>
        em.Add(em.Add(em.Mul(ax, bx), em.Mul(ay, by)), em.Mul(az, bz));

    /// <summary>Navy through sky blue, cream, amber and rust: one cosine per channel, a turn of 'along' going round once.</summary>
    private static Slot Palette(Emitter em, Slot along)
    {
        ReadOnlySpan<float> phases = [0.5f, 0.6f, 0.7f];

        var channels = new Slot[3];

        for (var c = 0; c < channels.Length; c++)
        {
            var wave = em.Unary(OpCode.Cos, em.Mul(em.Add(along, phases[c]), Tau));
            channels[c] = em.Add(em.Mul(wave, 0.5f), 0.5f);
        }

        return em.Combine(channels[0], channels[1], channels[2]);
    }

    /// <summary>
    /// One scene's distance function, with what it reads off the knobs worked out
    /// once so each step builds only what depends on where the ray is.
    /// </summary>
    private sealed class Shape
    {
        private readonly Emitter em;
        private readonly string scene;
        private readonly Slot twist, morph, spacing, inverse, size, thickness, wave;

        public Shape(Emitter em, string scene, EmitContext node)
        {
            this.em = em;
            this.scene = scene;

            var pulse = em.Ternary(OpCode.Clamp, node[PulsePort], em.Constant(0f), em.Constant(1f));

            twist = node[TwistPort];
            morph = em.Ternary(OpCode.Clamp, node[MorphPort], em.Constant(0f), em.Constant(1f));
            spacing = em.Ternary(OpCode.Clamp, node[SpacingPort], em.Constant(0.5f), em.Constant(16f));
            inverse = em.Binary(OpCode.Div, em.Constant(1f), spacing);

            size = scene switch
            {
                Tunnel => em.Mul(spacing, 0.6f),
                _ => em.Mul(spacing, em.Add(em.Mul(pulse, 0.12f), 0.2f)),
            };
            thickness = scene switch
            {
                Tunnel => em.Mul(spacing, em.Add(em.Mul(pulse, 0.06f), 0.05f)),
                _ => em.Add(em.Mul(pulse, 0.35f), 0.12f),
            };
            wave = em.Mul(inverse, Tau);
        }

        /// <summary>How far the point is from the nearest surface, never more.</summary>
        public Slot Distance(Slot x, Slot y, Slot z)
        {
            var (qx, qy) = Twisted(x, y, z);

            return scene switch
            {
                Tunnel => TunnelAt(qx, qy, z),
                Gyroid => GyroidAt(qx, qy, z),
                _ => LatticeAt(qx, qy, z),
            };
        }

        /// <summary>A shade off the hue for the surface at a point: each lattice cell its own, the rest by depth.</summary>
        public Slot Tone(Slot x, Slot y, Slot z)
        {
            var (qx, qy) = Twisted(x, y, z);

            if (scene != Lattice) return em.Mul(em.Mul(z, inverse), 0.07f);

            var cell = em.Add(
                em.Add(em.Mul(em.Unary(OpCode.Floor, em.Mul(qx, inverse)), 0.37f), em.Mul(em.Unary(OpCode.Floor, em.Mul(qy, inverse)), 0.71f)),
                em.Mul(em.Unary(OpCode.Floor, em.Add(em.Mul(z, inverse), 0.5f)), 0.13f));

            return em.Mul(em.Unary(OpCode.Fract, cell), 0.3f);
        }

        private (Slot X, Slot Y) Twisted(Slot x, Slot y, Slot z)
        {
            var angle = em.Mul(z, twist);
            var (cos, sin) = (em.Unary(OpCode.Cos, angle), em.Unary(OpCode.Sin, angle));

            return (em.Sub(em.Mul(x, cos), em.Mul(y, sin)), em.Add(em.Mul(x, sin), em.Mul(y, cos)));
        }

        /// <summary>
        /// A ball or a box in every cell, the camera's axis running down the gap
        /// between columns: x and y are counted from a corner of the cell, z from
        /// its middle.
        /// </summary>
        private Slot LatticeAt(Slot x, Slot y, Slot z)
        {
            var rx = Folded(x, 0f);
            var ry = Folded(y, 0f);
            var rz = Folded(z, 0.5f);

            var ball = em.Sub(em.Binary(OpCode.Hypot, em.Binary(OpCode.Hypot, rx, ry), rz), size);

            var reach = em.Binary(OpCode.Max, em.Binary(OpCode.Max, em.Unary(OpCode.Abs, rx), em.Unary(OpCode.Abs, ry)), em.Unary(OpCode.Abs, rz));
            var box = em.Sub(reach, em.Mul(size, 0.85f));

            return em.Ternary(OpCode.Mix, ball, box, morph);
        }

        /// <summary>A tube round the camera's axis, its wall rippling, with a ring every 'spacing'.</summary>
        private Slot TunnelAt(Slot x, Slot y, Slot z)
        {
            var round = em.Binary(OpCode.Hypot, x, y);
            var square = em.Binary(OpCode.Max, em.Unary(OpCode.Abs, x), em.Unary(OpCode.Abs, y));
            var across = em.Ternary(OpCode.Mix, round, square, morph);

            var ripple = em.Unary(OpCode.Sin, em.Add(em.Mul(em.Binary(OpCode.Atan2, y, x), 3f), em.Mul(z, 0.7f)));
            var radius = em.Add(size, em.Mul(em.Mul(ripple, spacing), 0.04f));

            var wall = em.Sub(radius, across);
            var ring = em.Sub(em.Binary(OpCode.Hypot, wall, Folded(z, 0.5f)), thickness);

            return em.Binary(OpCode.Min, wall, ring);
        }

        /// <summary>
        /// A gyroid sheet, or at full 'morph' a Schwarz surface, a period across
        /// each 'spacing'. The camera's axis lies a quarter period off the
        /// origin, where the gyroid never comes.
        /// </summary>
        private Slot GyroidAt(Slot x, Slot y, Slot z)
        {
            var gx = em.Mul(em.Add(x, em.Mul(spacing, 0.25f)), wave);
            var gy = em.Mul(y, wave);
            var gz = em.Mul(z, wave);

            var (sx, sy, sz) = (em.Unary(OpCode.Sin, gx), em.Unary(OpCode.Sin, gy), em.Unary(OpCode.Sin, gz));
            var (cx, cy, cz) = (em.Unary(OpCode.Cos, gx), em.Unary(OpCode.Cos, gy), em.Unary(OpCode.Cos, gz));

            var gyroid = em.Add(em.Add(em.Mul(sx, cy), em.Mul(sy, cz)), em.Mul(sz, cx));
            var schwarz = em.Mul(em.Add(em.Add(cx, cy), cz), 0.6f);
            var field = em.Ternary(OpCode.Mix, gyroid, schwarz, morph);

            // The field changes at most about 1.7 a radian, so this is a distance it never overstates.
            return em.Mul(em.Binary(OpCode.Div, em.Sub(em.Unary(OpCode.Abs, field), thickness), wave), 0.55f);
        }

        /// <summary>A coordinate folded into one cell, from minus half 'spacing' to plus half, offset by a share of a cell.</summary>
        private Slot Folded(Slot value, float offset) =>
            em.Mul(em.Add(em.Unary(OpCode.Fract, em.Add(em.Mul(value, inverse), offset)), -0.5f), spacing);
    }
}
