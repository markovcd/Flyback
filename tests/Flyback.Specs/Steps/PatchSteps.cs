using System.Globalization;
using System.Text.Json.Nodes;
using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// The patches the scenarios talk about, each built from a phrase a patch author
/// would use, and the edits made to them. Nothing here compiles: the steps that
/// look or listen do that.
/// </summary>
[Binding]
public sealed class PatchSteps(PatchContext context)
{
    /// <summary>Samples a loop needs before halving-plus-a-quarter sits on a half to within a sample's tolerance.</summary>
    private const int Settle = 30;

    // --- levels and colors ----------------------------------------------------

    [Given("a level of {float} on the screen")]
    public void GivenALevel(float level)
    {
        Level("level", level);
        Show("level");
    }

    [Given("a rainbow across the screen")]
    public void GivenARainbow()
    {
        Rainbow();
        Show("tint", "color");
    }

    [Given("pure red patched into an input that takes a single value")]
    public void GivenRedIntoASingleValue()
    {
        Rgb("red", 1f, 0f, 0f);
        context.Add("turn", "space.rotate");
        context.Wire("red", "color", "turn", "x");
        Show("turn", "x");
    }

    [Given("the color {float}, {float}, {float} halved by a Multiply")]
    public void GivenAColorHalved(float r, float g, float b)
    {
        Rgb("tint", r, g, b);
        context.Add("half", "math.mul");
        context.Wire("tint", "color", "half", "a");
        context.SetInput("half", "b", 0.5f);
        Show("half");
    }

    [Given("the color {float}, {float}, {float} split, with its green on the screen")]
    public void GivenAColorSplit(float r, float g, float b)
    {
        Rgb("tint", r, g, b);
        context.Add("channel", "color.split");
        context.Wire("tint", "color", "channel", "color");
        Show("channel", "g");
    }

    [Given("an adder whose knob reads {float}, with a level of {float} patched in over it")]
    public void GivenAnOverriddenAdder(float knob, float level)
    {
        context.Add("sum", "math.add");
        context.SetInput("sum", "a", knob);
        context.SetInput("sum", "b", 0f);
        Level("level", level);
        context.Wire("level", "out", "sum", "a");
        Show("sum");
    }

    [Then("the adder's knob still reads {float}")]
    public void ThenTheKnobIsKept(float expected) => context.StoredInput("sum", "a").ShouldBe(expected);

    // --- the Output -----------------------------------------------------------

    [Given("an Output with nothing patched into it")]
    public void GivenAnEmptyOutput() => context.Add("screen", "output");

    [Given("a sine with no Output to reach")]
    public void GivenNoOutput() => context.Add("wave", "osc.sine");

    [Given("a module from a newer Flyback patched to the screen")]
    public void GivenAnUnknownModule()
    {
        context.Add("screen", "output");
        context.AddUnknown("mystery", "module.from.the.future");
        context.Wire("mystery", 0, "screen", "color");
    }

    // --- space ----------------------------------------------------------------

    [Given("rings drawn across the screen")]
    public void GivenRings()
    {
        context.Add("coords", "coord");
        context.Add("rings", "pattern.rings");
        context.Wire("coords", "x", "rings", "x");
        context.Wire("coords", "y", "rings", "y");
        context.SetInput("rings", "freq", 3f);
        Show("rings");
    }

    [Given("a brightness that follows height on the screen")]
    public void GivenHeight()
    {
        context.Add("coords", "coord");
        context.Add("spread", "math.remap");
        context.Wire("coords", "y", "spread", "in");
        Show("spread");
    }

    [Given("a disc of radius one half on the screen")]
    public void GivenADisc()
    {
        context.Add("coords", "coord");
        context.Add("edge", "math.step");
        context.Wire("coords", "radius", "edge", "in");
        context.SetInput("edge", "edge", 0.5f);
        Show("edge");
    }

    // --- oscillators on the screen --------------------------------------------

    [Given("a sine on the screen with nothing patched into it")]
    public void GivenAFreeSine()
    {
        context.Add("osc", "osc.sine");
        Show("osc");
    }

    [Given("a sine on the screen driven by Time")]
    public void GivenASineOnTime()
    {
        context.Add("clock", "time");
        GivenAFreeSine();
        context.Wire("clock", "t", "osc", "in");
    }

    /// <summary>
    /// The domain is wired rather than left on its knob, because a normalled socket
    /// ignores its knob; what is on trial is amplitude and offset, not stored at all.
    /// </summary>
    [Given("a sine held at a quarter cycle, saved before it had amplitude and offset knobs")]
    public void GivenAnOldSine()
    {
        Level("knob", 0.25f);
        context.Add("osc", "osc.sine");
        context.SetInput("osc", "freq", 1f);
        context.Truncate("osc", 2);
        context.Wire("knob", "out", "osc", "in");
        Show("osc");
    }

    // --- feedback -------------------------------------------------------------

    [Given("feedback shown on the screen")]
    public void GivenFeedback()
    {
        context.Add("previous", "feedback");
        Show("previous", "color");
    }

    [Given("feedback brightened by {float} each frame")]
    public void GivenGrowingFeedback(float step)
    {
        Brightener();
        context.SetInput("brighten", "bias", step);
    }

    [Given("feedback brightened each frame by {float} plus one divided by zero")]
    public void GivenFeedbackWithADivisionByZero(float step)
    {
        Brightener();
        DivisionByZero();
        context.Add("offset", "math.add");
        context.Wire("broken", "out", "offset", "a");
        context.SetInput("offset", "b", step);
        context.Wire("offset", "out", "brighten", "bias");
    }

    // --- impossible arithmetic ------------------------------------------------

    [Given("a rainbow across the screen, brightened by one divided by zero")]
    public void GivenARainbowDividedByZero()
    {
        Rainbow();
        DivisionByZero();
        context.Add("brighten", "color.gain");
        context.Wire("tint", "color", "brighten", "color");
        context.SetInput("brighten", "gain", 1f);
        context.Wire("broken", "out", "brighten", "bias");
        Show("brighten", "color");
    }

    // --- a loop ---------------------------------------------------------------

    [Given("a loop that halves what it made last and adds a quarter")]
    public void GivenALoop()
    {
        context.Add("half", "math.mul");
        context.Add("nudge", "math.add");
        context.Add("screen", "output");
        context.SetInput("half", "b", 0.5f);
        context.SetInput("nudge", "b", 0.25f);
        context.Wire("half", "out", "nudge", "a");
        context.Wire("nudge", "out", "half", "a");
    }

    [Given("the loop is heard at the speakers")]
    public void GivenTheLoopIsHeard()
    {
        context.Wire("nudge", "out", "screen", "left");
        context.SetInput("screen", "volume", 1f);
    }

    [Given("the loop is shown on the screen")]
    public void GivenTheLoopIsShown() => context.Wire("nudge", "out", "screen", "color");

    [When("the loop has played until it settles")]
    public void WhenTheLoopSettles() => context.Play(Settle);

    [When("what it adds is turned to {float}")]
    public void WhenWhatItAddsIsTurned(float value) => context.SetInput("nudge", "b", value);

    // --- a tone ---------------------------------------------------------------

    [Given("a {float} Hz sine is playing")]
    public void GivenASine(float frequency)
    {
        context.Add("tone", "osc.sine");
        context.SetInput("tone", "freq", frequency);
        Hear("tone");
        context.HighestFrequency = frequency;
    }

    [Given("the module called {string} is playing")]
    public void GivenAModuleIsPlaying(string name) => Hear("it", Called(name));

    [Given("the module called {string} is on the screen")]
    public void GivenAModuleIsOnTheScreen(string name) => Show("it", Called(name));

    /// <summary>The module the catalog shows as <paramref name="name"/>, added as "it"; hands back its first output.</summary>
    private string Called(string name)
    {
        var def = NodeCatalog.All.Single(d => d.Name == name);
        context.Add("it", def.TypeId);
        return def.Outputs[0].Name;
    }

    /// <summary>Noise read off a fast clock, the way Overworld's hats are: 19200 new values a second.</summary>
    [Given("chip noise is playing")]
    public void GivenChipNoise()
    {
        context.Add("clock", NodeCatalog.TimeTypeId);
        context.Add("faster", "math.mul");
        context.SetInput("faster", "b", 300f);
        context.Add("noise", NodeCatalog.NoiseTypeId);
        context.SetInput("noise", "rate", 64f);
        context.Wire("clock", "t", "faster", "a");
        context.Wire("faster", "out", "noise", "in");
        Hear("noise", "random");
    }

    /// <summary>Filter and Delay are the engine's own (ADR-0128), so this needs no plugin.</summary>
    [Given("a {float} Hz sine through the engine's own filter and delay")]
    public void GivenAFilteredAndDelayedSine(float frequency)
    {
        context.Add("tone", "osc.sine");
        context.SetInput("tone", "freq", frequency);
        context.Add("filter", NodeCatalog.FilterTypeId);
        context.Add("delay", NodeCatalog.DelayTypeId);
        context.Wire("tone", "out", "filter", "in");
        context.Wire("filter", "low", "delay", "in");
        Hear("delay");
        context.HighestFrequency = frequency;
    }

    /// <summary>
    /// Everything an export can get wrong at once: noise needs its seed, the
    /// filter and the delay carry state, and feedback remembers the frame before.
    /// </summary>
    [Given("a drifting rainbow that trails, with noise through a filter and a delay at the speakers")]
    public void GivenAnEverythingPatch()
    {
        context.Add("clock", NodeCatalog.TimeTypeId);
        context.Add("coords", "coord");
        context.Add("drift", "math.add");
        context.Add("tint", "color.hsv");
        context.Add("dim", "color.gain");
        context.Add("previous", "feedback");
        context.Add("fade", "color.gain");
        context.Add("trail", "math.add");
        context.SetInput("dim", "gain", 0.5f);
        context.SetInput("fade", "gain", 0.5f);
        context.Wire("coords", "x", "drift", "a");
        context.Wire("clock", "t", "drift", "b");
        context.Wire("drift", "out", "tint", "hue");
        context.Wire("tint", "color", "dim", "color");
        context.Wire("previous", "color", "fade", "color");
        context.Wire("dim", "color", "trail", "a");
        context.Wire("fade", "color", "trail", "b");
        Show("trail");

        context.Add("noise", NodeCatalog.NoiseTypeId);
        context.Add("filter", NodeCatalog.FilterTypeId);
        context.Add("delay", NodeCatalog.DelayTypeId);
        context.Wire("clock", "t", "noise", "in");
        context.Wire("noise", "random", "filter", "in");
        context.Wire("filter", "low", "delay", "in");
        context.Wire("delay", "out", "screen", "left");
        context.SetInput("screen", "volume", 1f);
    }

    /// <summary>A Threshold on Time picks between the two pitches, so the frequency moves with no edit.</summary>
    [Given("a sine whose frequency jumps from {float} Hz to {float} Hz at {float} seconds")]
    public void GivenAJumpingSine(float from, float to, float seconds)
    {
        context.Add("clock", "time");
        context.Add("switch", "math.step");
        context.Add("pitch", "math.remap");
        context.Add("tone", "osc.sine");
        context.SetInput("switch", "edge", seconds);
        context.Wire("clock", "t", "switch", "in");
        context.Wire("switch", "out", "pitch", "in");
        context.SetInput("pitch", "in low", 0f);
        context.SetInput("pitch", "in high", 1f);
        context.SetInput("pitch", "out low", from);
        context.SetInput("pitch", "out high", to);
        context.Wire("pitch", "out", "tone", "freq");
        Hear("tone");
        context.HighestFrequency = Math.Max(from, to);
    }

    [Given("its frequency is turned to {float} Hz")]
    [When("its frequency is turned to {float} Hz")]
    public void WhenTheFrequencyIsTurned(float frequency)
    {
        context.SetInput("tone", "freq", frequency);
        context.HighestFrequency = Math.Max(context.HighestFrequency, frequency);
    }

    // --- timing and pitch, written in the text language ------------------------

    [Given(@"^a sequencer of ([\d., ]+) stepping (\d+) times a second$")]
    public void GivenASequencer(string values, int rate) =>
        Written($"values(rate: {rate}) [ {string.Join(' ', values.Split(',', StringSplitOptions.TrimEntries))} ] |> out.left");

    [Given("the gate of a sequencer stepping {int} times a second")]
    public void GivenASequencersGate(int rate) =>
        Written($"let s = values(rate: {rate}) [ 0.5 ]{(char)10}s.gate |> out.left");

    [Given("the gate of a sequencer stepping {int} times a second, through a chance of {float}")]
    public void GivenAGateThroughChance(int rate, float chance) => Chanced(rate, chance, "c.gate");

    [Given("the gate of a sequencer stepping {int} times a second, through a chance of {float}, with both of its gates added together")]
    public void GivenBothGatesOfChance(int rate, float chance) => Chanced(rate, chance, "c.gate + c.else");

    private void Chanced(int rate, float chance, string heard)
    {
        context.NotesPerSecond = rate;
        Written($"let s = values(rate: {rate}) [ 0.5 ]{(char)10}let c = s.gate |> chance(gate: _, chance: {Number(chance)}){(char)10}{heard} |> out.left");
    }

    [Given(@"^a sequencer of ([\d., ]+) stepping once a beat of a drum machine$")]
    public void GivenASequencerOnAClock(string values) =>
        Written(
            $"let c = midi.clock(device: \"{PatchContext.Machine}\"){(char)10}"
            + $"values(in: c.beats, rate: 1) [ {string.Join(' ', values.Split(',', StringSplitOptions.TrimEntries))} ] |> out.left");

    [Given("the gate of a MIDI In hearing channel {int} of a drum machine")]
    public void GivenAMidiInOnAChannel(int channel) =>
        Written($"midi.in(device: \"{PatchContext.Machine}\", voice: 1, channel: {channel}).gate |> out.left");

    /// <summary>
    /// A MIDI In per voice, each read as its pitch while its gate is open, so a
    /// voice that plays nothing reads nought. All of them reach the speakers,
    /// which is what makes the patch a many-voiced instrument to whoever plays it.
    /// </summary>
    [Given(@"^(one|two|three|four) (voices?|voices on automatic) listening to a keyboard$")]
    public void GivenVoices(string count, string kind)
    {
        var voices = Array.IndexOf(["one", "two", "three", "four"], count) + 1;
        var automatic = kind.EndsWith("automatic", StringComparison.Ordinal);

        context.Add("screen", "output");
        context.SetInput("screen", "volume", 1f);
        context.Voices = voices;

        for (var voice = 1; voice <= voices; voice++)
        {
            context.Add($"voice {voice}", NodeCatalog.MidiTypeId).SetState(
                MidiExtra.StateKey,
                new JsonObject
                {
                    [MidiExtra.DeviceField] = PatchContext.Keyboard,
                    [MidiExtra.IndexField] = automatic ? 0 : voice,
                });

            context.Add($"sounding {voice}", "math.mul");
            context.Wire($"voice {voice}", "pitch", $"sounding {voice}", "a");
            context.Wire($"voice {voice}", "gate", $"sounding {voice}", "b");

            if (voice == 1) continue;

            context.Add($"mix {voice}", "math.add");
            context.Wire(voice == 2 ? "sounding 1" : $"mix {voice - 1}", "out", $"mix {voice}", "a");
            context.Wire($"sounding {voice}", "out", $"mix {voice}", "b");
        }

        context.Wire(voices == 1 ? "sounding 1" : $"mix {voices}", "out", "screen", "left");
    }

    [Given("the {word} of a drum machine's clock on the speakers")]
    public void GivenAClockSignal(string signal) =>
        Written($"let c = midi.clock(device: \"{PatchContext.Machine}\"){(char)10}c.{signal} |> out.left");

    [Given(@"^a pitch of ([\d.]+) kept to (C major|all twelve notes)$")]
    public void GivenAPitchInKey(float pitch, string scale) =>
        Written($"value({Number(pitch)}) |> quantiser() [ {(scale == "C major" ? "C D E F G A B" : "C C# D D# E F F# G G# A A# B")} ] |> out.left");

    /// <summary>The gate is open from the start and closes at half a second.</summary>
    [Given("an envelope with a {int} ms attack, {int} ms decay, sustain of {float} and {int} ms release, held for half a second")]
    public void GivenAnEnvelope(int attack, int decay, float sustain, int release) =>
        Written($"adsr(gate: 1 - step(0.5, t), attack: {attack}ms, decay: {decay}ms, sustain: {Number(sustain)}, release: {release}ms) |> out.left");

    // --- switching off --------------------------------------------------------

    [Given("a level of {float} shown through a module that halves it")]
    public void GivenALevelHalved(float level)
    {
        Level("level", level);
        Halver("halve", "level");
        Show("halve");
    }

    [Given("a level of {float} shown through two modules that each halve it")]
    public void GivenALevelHalvedTwice(float level)
    {
        Level("level", level);
        Halver("halve", "level");
        Halver("halve again", "halve");
        Show("halve again");
    }

    [Given("a switched-off module with nothing patched in, feeding an adder set to {float} plus {float}")]
    public void GivenAnEmptyModuleFeedingAnAdder(float a, float b)
    {
        context.Add("halve", "math.mul");
        context.Add("sum", "math.add");
        context.SetInput("sum", "a", a);
        context.SetInput("sum", "b", b);
        context.Wire("halve", "out", "sum", "a");
        context.SwitchOff("halve");
        Show("sum");
    }

    [When("the halving module is switched off")]
    public void WhenTheHalverIsOff() => context.SwitchOff("halve");

    [When("both halving modules are switched off")]
    public void WhenBothHalversAreOff()
    {
        context.SwitchOff("halve");
        context.SwitchOff("halve again");
    }

    // --- ducking --------------------------------------------------------------

    [Given("a pad at {float} ducked by {float} under a kick at full level")]
    public void GivenADuckedPad(float pad, float depth)
    {
        Level("kick", 1f);
        Ducked(pad, depth);
    }

    /// <summary>The kick is one minus a Threshold on Time, so it is full until it stops.</summary>
    [Given("a pad at {float} ducked by {float} under a kick that stops after {float} seconds")]
    public void GivenAPadDuckedUnderAStoppingKick(float pad, float depth, float seconds)
    {
        context.Add("clock", "time");
        context.Add("stops", "math.step");
        context.Add("kick", "math.sub");
        context.SetInput("stops", "edge", seconds);
        context.Wire("clock", "t", "stops", "in");
        context.SetInput("kick", "a", 1f);
        context.Wire("stops", "out", "kick", "b");
        Ducked(pad, depth);
    }

    // --- buses ----------------------------------------------------------------

    [Given("a level of {float} is sent on the bus {string}")]
    public void GivenALevelIsSent(float level, string bus)
    {
        Level("level", level);
        Send("send", bus);
        context.Wire("level", "out", "send", "in");
    }

    [Given("the bus {string} is received at the speakers")]
    public void GivenTheBusIsHeard(string bus) => Hear(Receive(bus));

    [Given("two parts listening to the bus {string} are added together at the speakers")]
    public void GivenTwoPartsAreHeard(string bus)
    {
        context.Add("sum", "math.add");
        context.Wire(Receive(bus), "out", "sum", "a");
        context.Wire(Receive(bus), "out", "sum", "b");
        Hear("sum");
    }

    [Given("something else is sent on the bus {string}")]
    public void GivenSomethingElseIsSent(string bus)
    {
        Level("other level", 0.9f);
        Send("other send", bus);
        context.Wire("other level", "out", "other send", "in");
    }

    [Given("a bus that brings back what it carried and adds a quarter")]
    public void GivenALoopThroughABus()
    {
        context.Add("nudge", "math.add");
        context.SetInput("nudge", "b", 0.25f);
        Send("send", "loop");
        context.Wire(Receive("loop"), "out", "nudge", "a");
        context.Wire("nudge", "out", "send", "in");
        Hear("send");
    }

    // --- building blocks ------------------------------------------------------

    /// <summary>A patch written in the text language, heard at full volume.</summary>
    private void Written(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);
        load.Ok.ShouldBeTrue(load.Report);

        context.Replace(load.Patch);
        context.Name("screen", load.Patch.Nodes.Single(n => NodeCatalog.IsSink(n.TypeId)));
        context.SetInput("screen", "volume", 1f);
    }

    private static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

    // --- auto remap ----------------------------------------------------------

    [Given(@"^a sine at its (peak|trough) remapped onto red, from ([\d.]+) to ([\d.]+) of red's range$")]
    public void GivenASineRemappedOntoRed(string where, float low, float high)
    {
        context.Add("wave", "osc.sine");
        context.SetInput("wave", "freq", 0f);
        context.SetInput("wave", "phase", where == "peak" ? 0.25f : 0.75f);

        OntoRed("wave");
        context.SetInput("remap", "out low", low);
        context.SetInput("remap", "out high", high);
    }

    [Given(@"^a sine wired into a filter's (cutoff|input)$")]
    public void GivenASineIntoAFilter(string socket)
    {
        context.Add("wave", "osc.sine");
        context.Add("filter", "audio.filter");
        context.Wire("wave", "out", "filter", socket == "cutoff" ? "cutoff" : "in");
    }

    [Given("a sine wired into a color's brightness")]
    public void GivenASineIntoBrightness()
    {
        context.Add("wave", "osc.sine");
        context.Add("tint", "color.hsv");
        context.Wire("wave", "out", "tint", "value");
        Show("tint", "color");
    }

    /// <summary>An Auto remap from <paramref name="source"/> into the red of a color that has no green or blue, on the screen.</summary>
    private void OntoRed(string source)
    {
        context.Add("remap", "math.autoremap");
        Rgb("red", 0f, 0f, 0f);
        context.Wire(source, 0, "remap", "in");
        context.Wire("remap", "out", "red", "r");
        Show("red", "color");
    }

    private void Level(string name, float level)
    {
        context.Add(name, "value");
        context.SetInput(name, "value", level);
    }

    private void Rgb(string name, float r, float g, float b)
    {
        context.Add(name, "color.rgb");
        context.SetInput(name, "r", r);
        context.SetInput(name, "g", g);
        context.SetInput(name, "b", b);
    }

    private void Rainbow()
    {
        context.Add("coords", "coord");
        context.Add("tint", "color.hsv");
        context.Wire("coords", "x", "tint", "hue");
    }

    private void DivisionByZero()
    {
        context.Add("broken", "math.div");
        context.SetInput("broken", "a", 1f);
        context.SetInput("broken", "b", 0f);
    }

    private void Brightener()
    {
        context.Add("previous", "feedback");
        context.Add("brighten", "color.gain");
        context.Wire("previous", "color", "brighten", "color");
        context.SetInput("brighten", "gain", 1f);
        Show("brighten", "color");
    }

    private void PlusOneHalf()
    {
        context.Add("offset", "math.add");
        context.Wire("maths", "out", "offset", "a");
        context.SetInput("offset", "b", 0.5f);
        Show("offset");
    }

    private void Halver(string name, string from)
    {
        context.Add(name, "math.mul");
        context.SetInput(name, "b", 0.5f);
        context.Wire(from, "out", name, "a");
    }

    private void Show(string source, string port = "out")
    {
        context.Add("screen", "output");
        context.Wire(source, port, "screen", "color");
    }

    /// <summary>A pad under a Duck keyed by whatever is called "kick", recovering over a tenth of a second.</summary>
    private void Ducked(float pad, float depth)
    {
        Level("pad", pad);
        context.Add("duck", NodeCatalog.DuckTypeId);
        context.SetInput("duck", "depth", depth);
        context.SetInput("duck", "release", -1f);
        context.Wire("pad", "out", "duck", "left");
        context.Wire("kick", "out", "duck", "key");
        Hear("duck", "left");
    }

    private void Send(string name, string bus) => OnBus(context.Add(name, NodeCatalog.SendTypeId), bus);

    /// <summary>A fresh Receive on <paramref name="bus"/>, named for how many there are.</summary>
    private string Receive(string bus)
    {
        var name = $"receive {++receives}";
        OnBus(context.Add(name, NodeCatalog.ReceiveTypeId), bus);
        return name;
    }

    private int receives;

    private static void OnBus(NodeInstance node, string bus) =>
        node.SetState("bus", new JsonObject { ["bus"] = bus });

    private void Hear(string source, string port = "out")
    {
        context.Add("screen", "output");
        context.Wire(source, port, "screen", "left");
        context.SetInput("screen", "volume", 1f);
    }
}
