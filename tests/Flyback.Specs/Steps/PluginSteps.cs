using Reqnroll;
using Shouldly;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>Modules the shipped plugins add, on a patch built from the catalog an install loads.</summary>
[Binding]
public sealed class PluginSteps
{
    private const string Mandelbrot = "flyback.fractals.mandelbrot";
    private const string Julia = "flyback.fractals.julia";
    private const string Orbit = "flyback.fractals.orbit";

    /// <summary>Where the Mandelbrot module's picture is centered, and half its height, at zoom nought.</summary>
    private const float Middle = -0.75f;
    private const float Span = 1.25f;

    private const int Width = 64;
    private const int Height = 36;

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Modules = PluginHost.Load().Modules;

    private Frame? frame;
    private float[]? sound;

    [Given("the Mandelbrot set on the screen")]
    public void GivenTheSet() => Show(Mandelbrot, ("shift", 0f));

    [Given("the Mandelbrot set on the screen with its colors shifted by {float}")]
    public void GivenTheSetShifted(float shift) => Show(Mandelbrot, ("shift", shift));

    [Given("the Julia set of {float}, {float} on the screen")]
    public void GivenAJuliaSet(float re, float im) => Show(Julia, ("re", re), ("im", im));

    [Given("the orbit of {float}, {float} stepping {float} times a second")]
    public void GivenAnOrbit(float re, float im, float rate)
    {
        var b = new PatchBuilder(Modules);

        var orbit = Set(b.Add(Orbit), ("re", re), ("im", im), ("rate", rate));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(orbit, 0, output, NodeCatalog.OutputLeftPort);

        var program = b.Build().CompileForAudio(Modules).Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();

        sound = new float[Rate];

        for (var i = 0; i < sound.Length; i++)
        {
            program.Evaluate(0d, 0d, i / (double)Rate, registers, default, state);
            sound[i] = (float)registers[program.OutputBase];
        }
    }

    [Then("the point {float}, {float} of the plane is black")]
    public void ThenBlack(float re, float im) => At(re, im).ShouldBe((0f, 0f, 0f));

    [Then("the point {float}, {float} of the plane is blue")]
    public void ThenBlue(float re, float im) => IsBlue(At(re, im)).ShouldBeTrue($"{At(re, im)}");

    [Then("the point {float}, {float} of the plane is not blue")]
    public void ThenNotBlue(float re, float im) => IsBlue(At(re, im)).ShouldBeFalse($"{At(re, im)}");

    [Then("the middle of the screen is black")]
    public void ThenMiddleBlack() => frame.ShouldNotBeNull().AtFraction(0.5f, 0.5f).ShouldBe((0f, 0f, 0f));

    [Then("the middle of the screen is not black")]
    public void ThenMiddleNotBlack() => frame.ShouldNotBeNull().AtFraction(0.5f, 0.5f).ShouldNotBe((0f, 0f, 0f));

    /// <summary>
    /// A second in, the sound a period later is the same sound, and half a period
    /// later it is not.
    /// </summary>
    [Then("the sound repeats {float} times a second")]
    public void ThenItRepeats(float hertz)
    {
        var settled = sound.ShouldNotBeNull().AsSpan(Rate / 2);

        var period = (int)Math.Round(Rate / hertz);

        Rms(Apart(settled, period)).ShouldBeLessThan(Rms(settled) * 0.05);
        Rms(Apart(settled, period / 2)).ShouldBeGreaterThan(Rms(settled) * 0.2);
    }

    private void Show(string typeId, params (string Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Modules);

        var module = Set(b.Add(typeId), knobs);
        var output = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(module, 0, output, NodeCatalog.OutputColorPort);

        var program = b.Build().CompileForVideo(Modules).Program;
        var buffer = new byte[Width * 4 * Height];

        new SynthRenderer().Render(program, 0f, Width, Height, buffer, Width * 4);
        frame = new Frame(buffer, Width, Height);
    }

    private static NodeInstance Set(NodeInstance node, params (string Port, float Value)[] knobs)
    {
        var inputs = Modules.Require(node.TypeId).Inputs;

        foreach (var (port, value) in knobs)
            node.InputValues[inputs.Select((p, i) => (p.Name, i)).Single(p => p.Name == port).i] = value;

        return node;
    }

    /// <summary>The pixel a point of the plane lands on in the Mandelbrot's default view.</summary>
    private (float R, float G, float B) At(float re, float im)
    {
        var aspect = (float)Width / Height;

        var x = (re - Middle) / Span;
        var y = im / Span;

        return frame.ShouldNotBeNull().AtFraction((x + aspect) / (2f * aspect), (1f - y) / 2f);
    }

    private static bool IsBlue((float R, float G, float B) c) => c.B > 0.3f && c.B > c.R && c.B > c.G;

    private static float[] Apart(ReadOnlySpan<float> samples, int lag)
    {
        var difference = new float[samples.Length - lag];
        for (var i = 0; i < difference.Length; i++) difference[i] = samples[i + lag] - samples[i];
        return difference;
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        var sum = 0d;
        foreach (var s in samples) sum += s * (double)s;
        return Math.Sqrt(sum / samples.Length);
    }
}
