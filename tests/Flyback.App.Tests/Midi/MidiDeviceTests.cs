using Avalonia.Input;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Midi;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Midi;

/// <summary>
/// The hub with hardware behind it: which devices are offered, which are
/// actually opened, and what a note off one does to the programs that are
/// running.
/// </summary>
/// <remarks>
/// Against a stand-in backend rather than a device, because a build machine has
/// no keyboard plugged into it and the questions worth asking here are not about
/// a driver anyway. What a real one costs is one <see cref="IMidiInput"/> away,
/// and the winmm plugin's own tests cover the part that is: three bytes off a
/// wire, and what a device is called.
/// </remarks>
public class MidiDeviceTests
{
    private const string Device = "midi:test-keyboard";

    /// <summary>A block reading everything one instrument carries.</summary>
    private static LiveValues Reading(string source) => new(
    [
        MidiSignal.Key(source, MidiSignal.Pitch),
        MidiSignal.Key(source, MidiSignal.Gate),
        MidiSignal.Key(source, MidiSignal.Velocity),
        MidiSignal.Key(source, MidiSignal.Strikes),
    ]);

    private static double Read(LiveValues block, string source, string signal)
    {
        var key = MidiSignal.Key(source, signal);

        return block.At(block.Keys.ToList().IndexOf(key));
    }

    [Fact]
    public void The_computer_keyboard_comes_first_and_the_devices_after_it()
    {
        using var hub = new MidiHub(new FakeInput("Test Keyboard", "Other Thing"));

        hub.Sources.Select(s => s.Id)
            .ShouldBe([MidiSources.Keyboard, Device, "midi:other-thing"]);
    }

    /// <summary>
    /// With no backend at all the hub is what it always was, which is what every
    /// machine without a MIDI plugin gets.
    /// </summary>
    [Fact]
    public void With_no_backend_there_is_still_the_computer_keyboard()
    {
        using var hub = new MidiHub();

        hub.Sources.Select(s => s.Id).ShouldBe([MidiSources.Keyboard]);
    }

    /// <summary>
    /// A backend that throws while being asked what is plugged in is read as
    /// nothing plugged in. A half-installed driver must not stop a picker
    /// drawing, and there is always the computer's keys behind it.
    /// </summary>
    [Fact]
    public void A_backend_that_falls_over_while_listing_is_read_as_empty()
    {
        using var hub = new MidiHub(new BrokenInput());

        hub.Sources.Select(s => s.Id).ShouldBe([MidiSources.Keyboard]);
    }

    /// <summary>
    /// The device is held only while something is listening to it, which is what
    /// keeps a patch that merely has a MIDI In on the canvas from taking the
    /// keyboard away from whatever else is using it.
    /// </summary>
    [Fact]
    public void A_device_is_opened_only_once_a_program_reads_it()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        hub.Follow(Reading(MidiSources.Keyboard));
        backend.Opened.ShouldBeEmpty();

        hub.Follow(Reading(Device));
        backend.Opened.Select(p => p.Id).ShouldBe([Device]);
    }

    [Fact]
    public void A_recompile_that_stops_reading_it_hands_the_device_back()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        hub.Follow(Reading(Device));
        var port = backend.Opened.Single();

        hub.Follow(Reading(MidiSources.Keyboard));

        port.IsOpen.ShouldBeFalse();
        port.Closed.ShouldBe(1);
    }

    /// <summary>
    /// And is not handed back and taken again on every edit, which would be a
    /// device reopened sixty times a second while a knob is dragged.
    /// </summary>
    [Fact]
    public void A_recompile_that_still_reads_it_leaves_the_device_alone()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        hub.Follow(Reading(Device));
        hub.Follow(Reading(Device));
        hub.Follow(Reading(Device));

        backend.Opened.Count.ShouldBe(1);
        backend.Opened.Single().IsOpen.ShouldBeTrue();
    }

    /// <summary>A patch that wants only the note still opens the keyboard.</summary>
    [Fact]
    public void Reading_any_one_signal_is_enough_to_open_it()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        hub.Follow(new LiveValues([MidiSignal.Key(Device, MidiSignal.Pitch)]));

        backend.Opened.Count.ShouldBe(1);
    }

    [Fact]
    public void A_note_from_a_device_reaches_the_running_program()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);
        var block = Reading(Device);

        hub.Follow(block);
        backend.Opened.Single().Send(new MidiMessage(MidiAction.Down, 64, 0.5f));

        Read(block, Device, MidiSignal.Pitch).ShouldBe(64);
        Read(block, Device, MidiSignal.Gate).ShouldBe(1);
        Read(block, Device, MidiSignal.Velocity).ShouldBe(0.5, 0.0001);
        Read(block, Device, MidiSignal.Strikes).ShouldBe(1);
    }

    [Fact]
    public void A_device_and_the_computer_keyboard_are_two_instruments()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        var keys = Reading(MidiSources.Keyboard);
        var device = Reading(Device);

        hub.Follow(keys, device);

        hub.KeyDown(Key.Z);
        backend.Opened.Single().Send(new MidiMessage(MidiAction.Down, 64, 1f));

        // Each writes its own, and neither writes the other's.
        Read(keys, MidiSources.Keyboard, MidiSignal.Pitch).ShouldBe(48);
        Read(device, Device, MidiSignal.Pitch).ShouldBe(64);
    }

    [Fact]
    public void A_panic_from_a_device_lets_its_notes_go()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);
        var block = Reading(Device);

        hub.Follow(block);

        var port = backend.Opened.Single();
        port.Send(new MidiMessage(MidiAction.Down, 64, 1f));
        port.Send(new MidiMessage(MidiAction.AllOff, 0, 0f));

        Read(block, Device, MidiSignal.Gate).ShouldBe(0);
    }

    /// <summary>
    /// The one that would otherwise drone for the rest of the session. A device
    /// closed mid-chord sends no note-offs — there is nobody left to send them to
    /// — so the voice would go on holding a note nothing can ever release, and
    /// the next patch to listen to that device would open onto it already sounding.
    /// </summary>
    [Fact]
    public void A_note_held_when_a_device_closes_is_not_there_when_it_opens_again()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        var playing = Reading(Device);
        hub.Follow(playing);

        backend.Opened.Single().Send(new MidiMessage(MidiAction.Down, 64, 1f));
        Read(playing, Device, MidiSignal.Gate).ShouldBe(1);

        // Recompiled to a patch that listens to something else: the device is
        // handed back, mid-note and with no note-off to come.
        hub.Follow(Reading(MidiSources.Keyboard));
        backend.Opened[0].IsOpen.ShouldBeFalse();

        // And back again. Nothing is being held, so nothing sounds.
        var again = Reading(Device);
        hub.Follow(again);

        Read(again, Device, MidiSignal.Gate).ShouldBe(0);
        backend.Opened.Count.ShouldBe(2);
    }

    /// <summary>
    /// Losing the focus is about the computer's keys and nothing else. A MIDI
    /// keyboard goes on playing while another program is in front, and cutting a
    /// held chord off because somebody alt-tabbed would be a bug rather than a
    /// safeguard.
    /// </summary>
    [Fact]
    public void Losing_the_focus_does_not_silence_a_device()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        var keys = Reading(MidiSources.Keyboard);
        var device = Reading(Device);

        hub.Follow(keys, device);

        hub.KeyDown(Key.Z);
        backend.Opened.Single().Send(new MidiMessage(MidiAction.Down, 64, 1f));

        hub.AllOff();

        Read(keys, MidiSources.Keyboard, MidiSignal.Gate).ShouldBe(0);
        Read(device, Device, MidiSignal.Gate).ShouldBe(1);
    }

    [Fact]
    public void Closing_the_window_hands_every_device_back()
    {
        var backend = new FakeInput("Test Keyboard");
        var hub = new MidiHub(backend);

        hub.Follow(Reading(Device));
        var port = backend.Opened.Single();

        hub.Dispose();

        port.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// A device another program has taken. It is said out loud, because the patch
    /// goes on naming an instrument that is now silent and nothing else would
    /// explain why — and it is not a reason for the window to fall over.
    /// </summary>
    [Fact]
    public void A_device_that_will_not_open_is_reported_rather_than_thrown()
    {
        var backend = new FakeInput("Test Keyboard") { Refuse = true };
        using var hub = new MidiHub(backend);

        var said = new List<string>();
        hub.Trouble += said.Add;

        Should.NotThrow(() => hub.Follow(Reading(Device)));

        said.ShouldHaveSingleItem();
        said[0].ShouldContain("Test Keyboard");
    }

    /// <summary>A note played is a frame to draw, whichever instrument played it.</summary>
    [Fact]
    public void A_note_from_a_device_asks_for_a_frame()
    {
        var backend = new FakeInput("Test Keyboard");
        using var hub = new MidiHub(backend);

        hub.Follow(Reading(Device));

        var told = 0;
        hub.Played += () => told++;

        backend.Opened.Single().Send(new MidiMessage(MidiAction.Down, 64, 1f));

        told.ShouldBeGreaterThan(0);
    }

    /// <summary>A stand-in for a platform backend, with no platform behind it.</summary>
    private sealed class FakeInput(params string[] names) : IMidiInput
    {
        public List<FakePort> Opened { get; } = [];

        /// <summary>Whether opening fails, the way a device another program holds does.</summary>
        public bool Refuse { get; init; }

        public string Id => "fake";

        public string Name => "Stand-in";

        public int Priority => 1;

        public bool IsSupported => true;

        public IReadOnlyList<MidiPortInfo> Ports => MidiPorts.Named(names);

        public IMidiPort Open(string port, MidiCallback deliver)
        {
            if (Refuse) throw new InvalidOperationException("it is already in use.");

            var opened = new FakePort(port, deliver);

            Opened.Add(opened);

            return opened;
        }
    }

    private sealed class BrokenInput : IMidiInput
    {
        public string Id => "broken";

        public string Name => "Broken";

        public int Priority => 1;

        public bool IsSupported => true;

        public IReadOnlyList<MidiPortInfo> Ports => throw new InvalidOperationException("the driver is not well.");

        public IMidiPort Open(string port, MidiCallback deliver) => throw new InvalidOperationException();
    }

    private sealed class FakePort(string id, MidiCallback deliver) : IMidiPort
    {
        public string Id => id;

        public bool IsOpen { get; private set; } = true;

        /// <summary>How many times it was closed, because closing twice is its own bug.</summary>
        public int Closed { get; private set; }

        /// <summary>What the driver's thread would do, done on this one.</summary>
        public void Send(MidiMessage message) => deliver(message);

        public void Dispose()
        {
            IsOpen = false;
            Closed++;
        }
    }
}
