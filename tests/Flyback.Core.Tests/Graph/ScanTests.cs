using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Scan: a loop swept round the picture, and the value it passes over heard
/// as a waveform.
/// </summary>
/// <remarks>
/// The Probe read backwards, and everything here is about one of the two halves
/// of that. Its input is lowered under a domain of its own, like a Probe's — but
/// the domain substituted is a position rather than a moment, so what the sweep
/// produces is one sample per evaluation instead of one column per pixel. And
/// the two sinks disagree about where on the loop an evaluation sits, which is
/// settled on the memory flag rather than by lowering the subtree twice.
/// </remarks>
public class ScanTests
{
    private const string Scan = NodeCatalog.ScanTypeId;
    private const string Coordinates = "coord";
    private const string Rings = "pattern.rings";
    private const string Time = "time";

    // Sockets on the Scan, named for the same reason the Probe's are: a shifted
    // one would otherwise be a silent change of meaning here and in the catalogue.
    private const int In = 0;
    private const int Clock = 1;
    private const int Rate = 2;
    private const int Radius = 3;
    private const int CentreX = 4;
    private const int CentreY = 5;

    private const int Out = 0;
    private const int View = 1;

    /// <summary>
    /// A patch that scans <paramref name="watched"/> and sends the result
    /// wherever the caller asks — the speakers to hear the loop, the screen to
    /// see it.
    /// </summary>
    /// <param name="overCoordinates">
    /// Whether the source is a field, and so wants Coordinates in its first two
    /// sockets. Left off it is read at one point and there is nothing to scan —
    /// which is a mistake worth being able to make on purpose, and not one to
    /// make by accident in every test here.
    /// </param>
    private static (Patch Patch, NodeInstance Scanner) Scanning(
        string watched,
        int sourcePort = 0,
        int scanOutput = Out,
        int sinkPort = NodeCatalog.OutputLeftPort,
        bool overCoordinates = false,
        params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var clock = b.Add(Time, 0, 0, (0, 1f));
        var source = b.Add(watched, 300, 0);
        var scanner = b.Add(Scan, 600, 0, knobs);
        var output = b.Add(NodeCatalog.OutputTypeId, 900, 0, (NodeCatalog.OutputGainPort, 1f));

        if (overCoordinates)
        {
            var here = b.Add(Coordinates, 0, 200);
            b.Wire(here, 0, source, 0).Wire(here, 1, source, 1);
        }

        b.Wire(source, sourcePort, scanner, In)
         .Wire(clock, 0, scanner, Clock)
         .Wire(scanner, scanOutput, output, sinkPort);

        return (b.Patch, scanner);
    }

    /// <summary>One frame of the left channel, at unity gain.</summary>
    private static float[] Heard(Patch patch, int frames = AudioRenderer.DefaultSampleRate)
    {
        var buffer = new float[frames * 2];
        new AudioRenderer().Render(patch.CompileForAudio().Program, buffer, AudioScan.TimeDriven);

        var left = new float[frames];
        for (var frame = 0; frame < frames; frame++) left[frame] = buffer[frame * 2];
        return left;
    }

    /// <summary>What one pixel of the screen's program comes to, in linear RGB.</summary>
    private static (double R, double G, double B) Pixel(Patch patch, double x, double y, double t = 0d)
    {
        var program = patch.CompileForVideo().Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, t, registers, default);

        return (
            registers[program.OutputBase + 0],
            registers[program.OutputBase + 1],
            registers[program.OutputBase + 2]);
    }

    /// <summary>Zero crossings over one second, which is twice the frequency.</summary>
    private static int Crossings(float[] samples)
    {
        var count = 0;

        // Skip the start: the DC blocker settles from cold, and the memory flag
        // reads zero on the very first evaluation because nothing has written it
        // yet — so sample zero is the picture's answer rather than the ear's.
        for (var frame = 40; frame < samples.Length - 1; frame++)
        {
            var (a, b) = (samples[frame], samples[frame + 1]);
            if ((a < 0f && b >= 0f) || (a >= 0f && b < 0f)) count++;
        }

        return count;
    }

    /// <summary>
    /// What is left once the decimation filter has filled and the DC blocker has
    /// settled. Both are the renderer's, and neither is what a test of this
    /// module is trying to measure.
    /// </summary>
    private static float[] Settled(float[] samples) => [.. samples.Skip(samples.Length / 2)];

    [Fact]
    public void A_scan_is_an_ordinary_module_a_patch_may_hold_several_of()
    {
        var def = NodeCatalog.BuiltIn.Get(Scan).ShouldNotBeNull();

        new Patch().CanAdd(Scan).ShouldBeTrue();
        def.Outputs.Count.ShouldBe(2);
        def.Inputs[In].Swept.ShouldBeTrue();
        def.Inputs[Clock].Domain.ShouldBeTrue();
    }

    /// <summary>
    /// The sweep really does replace the coordinates: Coordinates read inside it
    /// hands back a point on the loop, so scanning x is a cosine at the rate.
    /// </summary>
    [Fact]
    public void Scanning_the_x_coordinate_gives_a_cosine_at_the_rate()
    {
        var (patch, _) = Scanning(
            Coordinates,
            knobs: [(Rate, 220f), (Radius, 0.8f)]);

        var samples = Heard(patch);

        // Two crossings a cycle, and the ends are ragged by a sample either way.
        Crossings(samples).ShouldBeInRange(438, 441);

        // The loop's radius is the amplitude, because the value being read *is*
        // the position — which is the whole claim about what got substituted.
        // Measured once the decimation filter has filled: from cold it rings
        // some 8% past the signal, which is the filter's business and not this
        // module's.
        Settled(samples).Max().ShouldBe(0.8f, 0.001f);
        Settled(samples).Min().ShouldBe(-0.8f, 0.001f);
    }

    /// <summary>
    /// The pitch is the loop rate and nothing else. A field with eightfold
    /// structure round the centre puts eight cycles in one turn, so the tone it
    /// makes is the eighth harmonic of the sweep rather than the sweep.
    /// </summary>
    [Fact]
    public void Structure_around_the_loop_becomes_the_harmonic()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var clock = b.Add(Time, 0, 0, (0, 1f));
        var coord = b.Add(Coordinates, 300, 0);

        // Sine of eight times the bearing: eight lobes round any circle centred
        // on the origin, whatever its radius.
        var lobes = b.Add("math.mul", 500, 0, (1, 8f));
        var wave = b.Add("math.sin", 700, 0);

        var scanner = b.Add(Scan, 900, 0, (Rate, 55f), (Radius, 0.5f));
        var output = b.Add(NodeCatalog.OutputTypeId, 1100, 0, (NodeCatalog.OutputGainPort, 1f));

        // Coordinates' fourth output is the angle, which inside the sweep is the
        // bearing of the point on the loop.
        b.Wire(coord, 3, lobes, 0)
         .Wire(lobes, 0, wave, 0)
         .Wire(wave, 0, scanner, In)
         .Wire(clock, 0, scanner, Clock)
         .Wire(scanner, Out, output, NodeCatalog.OutputLeftPort);

        // 55 turns a second, eight cycles a turn: 440 Hz, so 880 crossings.
        Crossings(Heard(b.Patch)).ShouldBeInRange(878, 882);
    }

    /// <summary>
    /// A loop that follows the field's own contours reads a constant, and a
    /// constant is not a sound. Rings are circles about the origin, so a scan
    /// centred there sits on one of them for the whole turn.
    /// </summary>
    [Fact]
    public void A_loop_along_a_contour_is_silent_and_moving_it_off_is_not()
    {
        var concentric = Scanning(
            Rings,
            overCoordinates: true,
            knobs: [(Rate, 220f), (Radius, 0.5f), (CentreX, 0f)]).Patch;

        var offset = Scanning(
            Rings,
            overCoordinates: true,
            knobs: [(Rate, 220f), (Radius, 0.5f), (CentreX, 0.7f)]).Patch;

        // Not merely quiet: the reading never changes over the turn, so the DC
        // blocker takes the whole of it.
        Settled(Heard(concentric, 9600)).Max(MathF.Abs).ShouldBeLessThan(1e-4f);

        Settled(Heard(offset, 9600)).Max(MathF.Abs).ShouldBeGreaterThan(0.2f);
    }

    /// <summary>
    /// The eye reads the loop at the pixel's own bearing, so the value at a
    /// pixel is the field at the point of the loop that pixel is looking at.
    /// Straight out to the right of the centre, that point is the far side of
    /// the loop on the x axis.
    /// </summary>
    [Fact]
    public void The_screen_reads_the_loop_at_the_pixels_bearing()
    {
        var (patch, _) = Scanning(
            Coordinates,
            scanOutput: Out,
            sinkPort: NodeCatalog.OutputColourPort,
            knobs: [(Radius, 0.6f)]);

        // Due east of the centre: the loop point is (0.6, 0), so scanning x
        // reads 0.6 — however far out the pixel itself is. The tolerance is a
        // knob's worth: the radius arrives as a float and is read as a double.
        Pixel(patch, 0.3, 0d).R.ShouldBe(0.6d, 1e-6d);
        Pixel(patch, 1.9, 0d).R.ShouldBe(0.6d, 1e-6d);

        // Due north: the loop point is (0, 0.6), so x there is nothing.
        Pixel(patch, 0d, 0.5).R.ShouldBe(0d, 1e-6d);
    }

    /// <summary>
    /// The display draws the loop where the loop runs. With nothing patched in
    /// the trace sits on the radius itself, so there is ink there and none in
    /// the middle.
    /// </summary>
    [Fact]
    public void The_view_draws_the_loop_where_it_runs()
    {
        var (patch, _) = Scanning(
            Coordinates,
            scanOutput: View,
            sinkPort: NodeCatalog.OutputColourPort,
            knobs: [(Radius, 0.6f), (CentreX, 0f), (CentreY, 0f)]);

        // Green is the phosphor, so it carries the most of whatever ink is here.
        Pixel(patch, 0d, 0.6).G.ShouldBeGreaterThan(0.5d);
        Pixel(patch, 0d, 0.2).G.ShouldBeLessThan(0.05d);
    }

    /// <summary>
    /// One lowering, not two. The sweep is resolved once and the bearing chosen
    /// arithmetically, so a Scan costs one copy of whatever it is scanning
    /// however different the two sinks make of it.
    /// </summary>
    [Fact]
    public void The_scanned_subtree_is_lowered_once()
    {
        var (patch, _) = Scanning(
            Rings,
            scanOutput: Out,
            sinkPort: NodeCatalog.OutputColourPort,
            overCoordinates: true);

        var program = patch.CompileForVideo().Program;

        // Three, and each is accounted for: the Rings' own radius, the
        // Coordinates feeding it — which emits all four of its outputs whether
        // or not a wire takes them — and the Scan's display measuring the pixel
        // off the centre. A subtree lowered once for the ear and again for the
        // eye would put a fourth here.
        program.Ops.Count(op => op.Code == OpCode.Hypot).ShouldBe(3);
    }

    /// <summary>
    /// The shipped patch is audible, and is worth pinning because the way it
    /// would stop being so is invisible: nothing about a loop sitting on a
    /// contour looks wrong on screen, and the picture is identical either side
    /// of the mistake.
    /// </summary>
    [Fact]
    public void The_ring_scan_preset_makes_a_sound()
    {
        var samples = Heard(Presets.RingScan(NodeCatalog.BuiltIn));

        Settled(samples).Max(MathF.Abs).ShouldBeGreaterThan(0.1f);
    }
}
