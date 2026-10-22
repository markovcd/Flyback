using System.Text.Json.Nodes;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// The Speech plugin's Speak, played for two seconds with its trigger on a wire,
/// and its Mouth, drawn alone.
/// </summary>
[Binding]
public sealed class SpeechSteps
{
    private const string Speak = "flyback.speech.speak";
    private const string Mouth = "flyback.speech.mouth";

    private const int Width = 160;
    private const int Height = 90;

    private const int Rate = GlobalConstants.SampleRate;

    private static ModuleCatalog Modules => ShippedPlugins.Loaded.Modules;

    private float[]? sound;
    private IReadOnlyList<CompileIssue> issues = [];
    private Frame? frame;

    [Given("a Speak with nothing wired")]
    public void GivenASpeak() => Play(Create());

    [Given("a Speak saying {string} once")]
    public void GivenASpeakSaying(string words) => Play(Create(words, ("every", 0f)));

    [Given("a Speak saying {string} once, triggered at {float} seconds")]
    public void GivenASpeakTriggered(string words, float at) => Play(Create(words, ("every", 0f)), at);

    [Given("a Speak singing {string} to {string} at {float} beats a minute")]
    public void GivenASpeakSinging(string words, string notes, float bpm)
    {
        var speak = Create(words, ("bpm", bpm));
        speak.SetState("speak", new JsonObject { ["words"] = words, ["mode"] = "sing", ["notes"] = notes });

        Play(speak);
    }

    [Given("a Mouth open {float}")]
    public void GivenAMouth(float open)
    {
        var def = Modules.Require(Mouth);
        var b = new PatchBuilder(Modules);

        var mouth = b.Add(Mouth, (def.Inputs.ToList().FindIndex(p => p.Name == "open"), open), (def.Inputs.ToList().FindIndex(p => p.Name == "size"), 1f));
        b.Wire(mouth, 0, b.Add(NodeCatalog.OutputTypeId), NodeCatalog.OutputColorPort);

        var buffer = new byte[Width * 4 * Height];
        new SynthRenderer().Render(b.Build().CompileForVideo(Modules).Program, 0f, Width, Height, buffer, Width * 4);
        frame = new Frame(buffer, Width, Height);
    }

    [Given("a Speak with every knob turned all the way up")]
    public void GivenASpeakFlatOut()
    {
        var speak = Create("Hello. [aa aa ae ae ao ao er er]");
        var inputs = Modules.Require(Speak).Inputs;

        for (var i = 1; i < inputs.Count; i++)
            if (inputs[i].Name != "trigger") speak.InputValues[i] = inputs[i].Max;

        Play(speak);
    }

    [Then("it is heard within the first second")]
    public void ThenHeardAtFirst() => Rms(0f, 1f).ShouldBeGreaterThan(0.02);

    [Then("it is heard from {float} seconds to {float}")]
    public void ThenHeard(float from, float to) => Rms(from, to).ShouldBeGreaterThan(0.02);

    [Then("it is silent from {float} second to {float}")]
    public void ThenSilent(float from, float to) => Rms(from, to).ShouldBeLessThan(1e-4);

    [Then("building it warns about {string}")]
    public void ThenWarns(string symbol) =>
        issues.ShouldContain(i => i.Severity == IssueSeverity.Warning && i.Message.Contains(symbol));

    [Then("its pitch at {float} seconds is {float} Hz")]
    public void ThenPitch(float at, float hz) => Pitch(at).ShouldBe(hz, hz * 0.03);

    [Then("the point {float}, {float} of the screen is lip red")]
    public void ThenLipRed(float x, float y)
    {
        var (r, g, b) = At(x, y);
        (r > 0.5f && r > 2f * g && r > 2f * b).ShouldBeTrue($"{(r, g, b)}");
    }

    [Then("the point {float}, {float} of the screen is dark red")]
    public void ThenDarkRed(float x, float y)
    {
        var (r, g, b) = At(x, y);
        (r is > 0.05f and < 0.3f && r > g && r > b).ShouldBeTrue($"{(r, g, b)}");
    }

    [Then("the point {float}, {float} of the screen is black")]
    public void ThenBlack(float x, float y) => At(x, y).ShouldBe((0f, 0f, 0f));

    [Then("its sound never goes past full scale")]
    public void ThenInsideFullScale()
    {
        foreach (var sample in sound.ShouldNotBeNull()) sample.ShouldBeInRange(-1f, 1f);
    }

    private static NodeInstance Create(string? words = null, params (string Port, float Value)[] knobs)
    {
        var def = Modules.Require(Speak);
        var speak = NodeInstance.Create(def, 0, 0);

        if (words is not null) speak.SetState("speak", new JsonObject { ["words"] = words });
        foreach (var (port, value) in knobs) speak.InputValues[def.Inputs.ToList().FindIndex(p => p.Name == port)] = value;

        return speak;
    }

    /// <summary>Two seconds of <paramref name="speak"/>, its trigger rising at <paramref name="trigger"/> if one is given.</summary>
    private void Play(NodeInstance speak, float? trigger = null)
    {
        var b = new PatchBuilder(Modules);
        b.Patch.Nodes.Add(speak);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(speak, 0, output, NodeCatalog.OutputLeftPort);

        // The trigger arrives as x, the one input a step can vary per sample.
        if (trigger is not null) b.Wire(b.Add("coord"), 0, speak, 4);

        var compiled = b.Build().CompileForAudio(Modules);
        issues = compiled.Issues;

        var program = compiled.Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();

        sound = new float[Rate * 2];

        for (var i = 0; i < sound.Length; i++)
        {
            var seconds = i / (double)Rate;
            var x = trigger is { } at && seconds >= at ? 1d : 0d;

            program.Evaluate(x, 0d, seconds, registers, default, state);
            sound[i] = (float)registers[program.OutputBase];
        }
    }

    /// <summary>
    /// The pitch of the 50 ms from <paramref name="at"/>: the shortest shift at which it
    /// all but matches itself, since every whole number of periods matches as well.
    /// </summary>
    private double Pitch(float at)
    {
        var span = sound.ShouldNotBeNull().AsSpan((int)(at * Rate), Rate / 20);
        var matches = new double[Rate / 60];

        for (var lag = Rate / 500; lag < matches.Length; lag++)
        {
            for (var i = 0; i + lag < span.Length; i++) matches[lag] += span[i] * (double)span[i + lag];
            matches[lag] /= span.Length - lag;
        }

        var best = matches.Max();
        var first = Array.FindIndex(matches, Rate / 500, m => m >= best * 0.9);

        while (first + 1 < matches.Length && matches[first + 1] > matches[first]) first++;

        return Rate / (double)first;
    }

    /// <summary>The pixel under a point of the picture, x across and y up, y running -1 to 1.</summary>
    private (float R, float G, float B) At(float x, float y)
    {
        var aspect = (float)Width / Height;

        return frame.ShouldNotBeNull().AtFraction((x + aspect) / (2f * aspect), (1f - y) / 2f);
    }

    private double Rms(float from, float to)
    {
        var span = sound.ShouldNotBeNull().AsSpan((int)(from * Rate), (int)((to - from) * Rate));
        var sum = 0d;
        foreach (var sample in span) sum += sample * (double)sample;
        return Math.Sqrt(sum / span.Length);
    }
}
