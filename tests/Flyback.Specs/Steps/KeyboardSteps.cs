using System.Text.RegularExpressions;
using Reqnroll;
using Shouldly;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Midi;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>
/// A keyboard played into a patch through the hub the editor plays with, and what
/// each of the patch's voices is sounding.
/// </summary>
/// <remarks>
/// The keyboard is a stand-in device, because the question is which voice a note
/// lands on, not what a driver makes of a cable.
/// </remarks>
[Binding]
public sealed partial class KeyboardSteps(PatchContext context) : IDisposable
{
    private const string Note = @"[A-G]#?-?\d";
    private const string Notes = $@"({Note}(?:, {Note})*(?: and {Note})?)";

    private readonly StandIn keyboard = new();
    private readonly List<(CompiledPatch Program, LiveValues Live)> probes = [];
    private MidiHub? hub;

    [When($@"^{Notes} (?:is|are) (?:held|played)$")]
    public void WhenHeld(string notes)
    {
        foreach (var note in Parse(notes)) Send(new MidiMessage(MidiAction.Down, note, 0.8f));
    }

    [When($@"^{Notes} (?:is|are) let go$")]
    public void WhenLetGo(string notes)
    {
        foreach (var note in Parse(notes)) Send(new MidiMessage(MidiAction.Up, note, 0f));
    }

    /// <summary>What a key held long enough to auto-repeat sends, or a device that says a note-on twice.</summary>
    [When($@"^the keyboard sends {Notes} again without letting it go$")]
    public void WhenSentAgain(string notes) => WhenHeld(notes);

    [Then($@"^the voices? plays? ((?:{Note}|nothing)(?:, (?:{Note}|nothing))*(?: and (?:{Note}|nothing))?)$")]
    public void ThenTheVoicesPlay(string expected)
    {
        var wanted = Split(expected).Select(n => n == "nothing" ? 0d : NoteNumber(n)).ToArray();

        wanted.Length.ShouldBe(context.Voices);
        Sounding().ShouldBe(wanted, $"voices 1 to {context.Voices}, as note numbers with nought for silent");
    }

    /// <summary>
    /// Which note each voice is sounding: its MIDI In's pitch while the gate is
    /// open, read from a program rooted at that voice, as a Probe reads one.
    /// </summary>
    private double[] Sounding() =>
    [
        .. probes.Select(probe =>
        {
            var registers = probe.Program.AllocateRegisters();
            probe.Program.Evaluate(0d, 0d, 0d, registers, default, live: probe.Live);
            return registers[probe.Program.OutputBase];
        }),
    ];

    private void Send(MidiMessage message)
    {
        Follow();
        keyboard.Port.ShouldNotBeNull("nothing in the patch opened the keyboard").Send(message);
    }

    /// <summary>
    /// Points the hub at the programs the editor would be running: the sound, which
    /// reads every voice and so decides which ones a note may land on, and a probe
    /// on each voice to watch it by.
    /// </summary>
    private void Follow()
    {
        if (hub is not null) return;

        hub = new MidiHub(keyboard);

        var sound = context.Patch.CompileForAudio(played: true).Program;

        for (var voice = 1; voice <= context.Voices; voice++)
        {
            var probe = context.Patch.CompileForProbe(context.Node($"sounding {voice}").Id, played: true).Program;
            probes.Add((probe, new LiveValues(probe.LiveInputs)));
        }

        hub.Follow([new LiveValues(sound.LiveInputs), .. probes.Select(p => p.Live)]);
    }

    private static IEnumerable<int> Parse(string notes) => Split(notes).Select(NoteNumber);

    private static IEnumerable<string> Split(string list) =>
        list.Replace(" and ", ", ", StringComparison.Ordinal).Split(", ");

    /// <summary>A note's name as MIDI numbers it, middle C being C4 and 60.</summary>
    private static int NoteNumber(string name)
    {
        var match = NoteName().Match(name);
        var semitone = "C D EF G A B".IndexOf(match.Groups[1].Value[0], StringComparison.Ordinal);

        return 12 * (int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) + 1)
            + semitone + (match.Groups[2].Success ? 1 : 0);
    }

    [GeneratedRegex(@"^([A-G])(#)?(-?\d)$")]
    private static partial Regex NoteName();

    public void Dispose() => hub?.Dispose();

    /// <summary>A MIDI backend with one keyboard plugged in, and nothing behind it.</summary>
    private sealed class StandIn : IMidiInput
    {
        public Played? Port { get; private set; }

        public string Id => "stand-in";

        public string Name => "Stand-in";

        public int Priority => 1;

        public bool IsSupported => true;

        public IReadOnlyList<MidiPortInfo> Ports => MidiPorts.Named(["Stage Piano"]);

        public IMidiPort Open(string port, MidiCallback deliver) => Port = new Played(port, deliver);
    }

    /// <summary>The keyboard, sending on this thread what a driver would send on its own.</summary>
    private sealed class Played(string id, MidiCallback deliver) : IMidiPort
    {
        public string Id => id;

        public bool IsOpen { get; private set; } = true;

        public void Send(MidiMessage message) => deliver(message);

        public void Dispose() => IsOpen = false;
    }
}
