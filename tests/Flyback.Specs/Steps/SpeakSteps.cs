using System.Text.Json.Nodes;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The Voice plugin's Speak, played for two seconds with its trigger on a wire.</summary>
[Binding]
public sealed class SpeakSteps
{
    private const string Speak = "flyback.voice.speak";

    private const int Rate = GlobalConstants.SampleRate;

    private static ModuleCatalog Modules => ShippedPlugins.Loaded.Modules;

    private float[]? sound;
    private IReadOnlyList<CompileIssue> issues = [];

    [Given("a Speak with nothing wired")]
    public void GivenASpeak() => Play(Create());

    [Given("a Speak saying {string} once")]
    public void GivenASpeakSaying(string words) => Play(Create(words, ("every", 0f)));

    [Given("a Speak saying {string} once, triggered at {float} seconds")]
    public void GivenASpeakTriggered(string words, float at) => Play(Create(words, ("every", 0f)), at);

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

    private double Rms(float from, float to)
    {
        var span = sound.ShouldNotBeNull().AsSpan((int)(from * Rate), (int)((to - from) * Rate));
        var sum = 0d;
        foreach (var sample in span) sum += sample * (double)sample;
        return Math.Sqrt(sum / span.Length);
    }
}
