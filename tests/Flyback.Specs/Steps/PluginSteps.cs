using Flyback.Engine.Compile;
using Reqnroll;
using Shouldly;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>Modules the shipped plugins add, on a patch built from the catalog an install loads.</summary>
[Binding]
public sealed class PluginSteps
{
    private const string Mandelbrot = "flyback.fractals.mandelbrot";
    private const string Julia = "flyback.fractals.julia";
    private const string Orbit = "flyback.fractals.orbit";
    private const string EasySynth = "flyback.easy.synth";
    private const string EasyDrum = "flyback.easy.drummer";

    /// <summary>Where the Mandelbrot module's picture is centered, and half its height, at zoom nought.</summary>
    private const float Middle = -0.75f;
    private const float Span = 1.25f;

    private const int Width = 64;
    private const int Height = 36;

    private const int Rate = GlobalConstants.SampleRate;

    private static ModuleCatalog Modules => ShippedPlugins.Loaded.Modules;

    private Frame? frame;
    private float[]? sound;
    private float[]? right;
    private double[]? hits;

    [Given("the Mandelbrot set on the screen")]
    public void GivenTheSet() => Show(Mandelbrot, ("shift", 0f));

    [Given("the Mandelbrot set on the screen with its colors shifted by {float}")]
    public void GivenTheSetShifted(float shift) => Show(Mandelbrot, ("shift", shift));

    [Given("the Julia set of {float}, {float} on the screen")]
    public void GivenAJuliaSet(float re, float im) => Show(Julia, ("re", re), ("im", im));

    [Given("the orbit of {float}, {float} stepping {float} times a second")]
    public void GivenAnOrbit(float re, float im, float rate) =>
        Play(Set(NodeInstance.Create(Modules.Require(Orbit), 0, 0), ("re", re), ("im", im), ("rate", rate)));

    [Given("the Julia orbit of {float}, {float} from the pixel {float}, {float} stepping {float} times a second")]
    public void GivenAJuliaOrbit(float re, float im, float startRe, float startIm, float rate)
    {
        var orbit = Set(
            NodeInstance.Create(Modules.Require(Orbit), 0, 0),
            ("re", re), ("im", im), ("start re", startRe), ("start im", startIm), ("rate", rate));

        orbit.SetState("orbit", new System.Text.Json.Nodes.JsonObject { ["mode"] = "julia" });

        Play(orbit);
    }

    [Given("an Easy Synth with nothing wired")]
    public void GivenAnEasySynth() => PlayBoth(Easy());

    [Given("an Easy Synth playing a {word} at note {float}")]
    public void GivenAnEasySynthPlaying(string wave, float note) =>
        PlayBoth(Set(Easy(("wave", wave), ("filter", "off")), ("pitch", note)));

    [Given("an Easy Synth with every knob but its attack turned all the way up")]
    public void GivenAnEasySynthFlatOut()
    {
        var synth = Easy(("wave", "supersaw"), ("filter", "band"));
        var inputs = Modules.Require(EasySynth).Inputs;

        for (var i = 1; i < inputs.Count; i++)
            synth.InputValues[i] = inputs[i].Name == "attack" ? inputs[i].Min : inputs[i].Max;

        PlayBoth(synth);
    }

    [Given("an Easy Synth whose note has been let go")]
    public void GivenAnEasySynthLetGo() => PlayBoth(Set(Easy(), ("gate", 0f)));

    [Given("an Easy Drum with nothing wired")]
    public void GivenAnEasyDrum() => PlayBoth(Drum());

    [Given("an Easy Drum playing {string}")]
    public void GivenAnEasyDrumPlaying(string sound) => PlayBoth(Drum(("sound", sound)));

    [Given("an Easy Drum on Trigger with nothing wired")]
    public void GivenAnEasyDrumOnTrigger() => PlayBoth(Drum(("rhythm", "trigger")));

    [Given("an Easy Drum with every knob turned all the way up")]
    public void GivenAnEasyDrumFlatOut()
    {
        var drum = Drum(("sound", "open hat"), ("rhythm", "sixteenths"));
        var inputs = Modules.Require(EasyDrum).Inputs;

        for (var i = 1; i < inputs.Count; i++) drum.InputValues[i] = inputs[i].Max;

        PlayBoth(drum);
    }

    private static NodeInstance Drum(params (string Key, string Value)[] settings)
    {
        var drum = NodeInstance.Create(Modules.Require(EasyDrum), 0, 0);

        var state = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in settings) state[key] = value;
        drum.SetState("drum", state);

        return drum;
    }

    /// <summary>One second of <paramref name="synth"/>, its 'left' on the left and its 'right' on the right.</summary>
    private void PlayBoth(NodeInstance synth)
    {
        var b = new PatchBuilder(Modules);
        b.Patch.Nodes.Add(synth);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(synth, 0, output, NodeCatalog.OutputLeftPort).Wire(synth, 1, output, NodeCatalog.OutputRightPort);

        var program = b.Build().CompileForAudio(Modules).Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();

        sound = new float[Rate];
        right = new float[Rate];

        for (var i = 0; i < sound.Length; i++)
        {
            program.Evaluate(0d, 0d, i / (double)Rate, registers, default, state);
            sound[i] = (float)registers[program.OutputBase];
            right[i] = (float)registers[program.OutputBase + 1];
        }

        hits = synth.TypeId == EasyDrum ? Hits(synth) : null;
    }

    /// <summary>The seconds of each hit in two seconds of a drum, where its 'env' jumps up.</summary>
    private static double[] Hits(NodeInstance drum)
    {
        var b = new PatchBuilder(Modules);
        b.Patch.Nodes.Add(drum);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(drum, 2, output, NodeCatalog.OutputLeftPort);

        var program = b.Build().CompileForAudio(Modules).Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();

        var found = new List<double>();
        var previous = 0d;

        for (var i = 0; i < 2 * Rate; i++)
        {
            program.Evaluate(0d, 0d, i / (double)Rate, registers, default, state);

            var env = registers[program.OutputBase];
            if (env - previous > 0.3) found.Add(i / (double)Rate);
            previous = env;
        }

        return [.. found];
    }

    private static NodeInstance Easy(params (string Key, string Value)[] settings)
    {
        var synth = NodeInstance.Create(Modules.Require(EasySynth), 0, 0);

        var state = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in settings) state[key] = value;
        synth.SetState("synth", state);

        return synth;
    }

    /// <summary>One second of <paramref name="orbit"/>'s left side, sample by sample.</summary>
    private void Play(NodeInstance orbit)
    {
        var b = new PatchBuilder(Modules);
        b.Patch.Nodes.Add(orbit);

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

    [Then("the sound is heard")]
    public void ThenHeard() =>
        Math.Max(Rms(sound.ShouldNotBeNull()), Rms(right.ShouldNotBeNull())).ShouldBeGreaterThan(0.05);

    [Then("the sound never goes past full scale")]
    public void ThenInsideFullScale()
    {
        foreach (var sample in sound.ShouldNotBeNull().Concat(right.ShouldNotBeNull())) sample.ShouldBeInRange(-1f, 1f);
    }

    [Then("the sound has faded to silence")]
    public void ThenFaded()
    {
        Rms(sound.ShouldNotBeNull().AsSpan(Rate / 2)).ShouldBeLessThan(1e-4);
        Rms(right.ShouldNotBeNull().AsSpan(Rate / 2)).ShouldBeLessThan(1e-4);
    }

    [Then(@"^it plays at (.+) seconds in the first two$")]
    public void ThenItPlaysAt(string seconds)
    {
        var expected = seconds.Split([",", " and "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(text => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var found = hits.ShouldNotBeNull();

        found.Length.ShouldBe(expected.Length, string.Join(", ", found));
        for (var i = 0; i < expected.Length; i++) found[i].ShouldBe(expected[i], 0.002);
    }

    [Then("the sound is silent")]
    public void ThenSilent()
    {
        Rms(sound.ShouldNotBeNull()).ShouldBe(0d);
        Rms(right.ShouldNotBeNull()).ShouldBe(0d);
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
