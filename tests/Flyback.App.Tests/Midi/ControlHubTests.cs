using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Midi;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Midi;

/// <summary>
/// The panel's knobs with a stand-in controller behind them: what a controller
/// moving does to the running programs, when it is ignored, and how a knob learns one.
/// </summary>
public class ControlHubTests
{
    private const string Device = "midi:test-controller";

    private static (Patch Patch, PatchControl Knob) Patched(float value = 0.5f, MidiBinding? binding = null)
    {
        var patch = new Patch();
        var knob = patch.AddControl(value: value);
        knob.Midi = binding;

        return (patch, knob);
    }

    private static double Read(LiveValues block, PatchControl knob) =>
        block.At(block.Keys.ToList().IndexOf(knob.Key));

    private static MidiMessage Cc(int controller, int value, int channel = 1) =>
        new(MidiAction.Control, controller, value / 127f) { Channel = channel };

    [Fact]
    public void Following_a_patch_seeds_every_block_with_where_its_knobs_rest()
    {
        using var midi = new MidiHub(new FakeInput("Test Controller"));
        var hub = new ControlHub(midi);
        var (patch, knob) = Patched(0.3f);
        var block = new LiveValues([knob.Key]);

        hub.Follow(patch, block);

        Read(block, knob).ShouldBe(0.3d, 1e-6);
    }

    [Fact]
    public void A_bound_device_is_held_open_though_no_program_reads_its_notes()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);
        var (patch, _) = Patched(binding: new MidiBinding(Device, 0, 21));

        hub.Follow(patch, new LiveValues([]));

        backend.Opened.Select(p => p.Id).ShouldBe([Device]);
    }

    [Fact]
    public void A_controller_moving_turns_the_knob_it_is_bound_to()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);
        var (patch, knob) = Patched(binding: new MidiBinding(Device, 0, 21));
        var block = new LiveValues([knob.Key]);
        var turned = new List<(Guid, float)>();
        hub.Turned += (id, value) => turned.Add((id, value));

        hub.Follow(patch, block);
        backend.Opened.Single().Send(Cc(21, 127));

        Read(block, knob).ShouldBe(1d, 1e-6);
        turned.ShouldBe([(knob.Id, 1f)]);
    }

    [Fact]
    public void Another_controller_leaves_it_alone()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);
        var (patch, knob) = Patched(0.5f, new MidiBinding(Device, 0, 21));
        var block = new LiveValues([knob.Key]);

        hub.Follow(patch, block);
        backend.Opened.Single().Send(Cc(22, 127));

        Read(block, knob).ShouldBe(0.5d, 1e-6);
    }

    [Fact]
    public void Picking_up_ignores_a_controller_until_it_passes_the_knob()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi) { Takeover = Takeover.PickUp };
        var (patch, knob) = Patched(0.5f, new MidiBinding(Device, 0, 21));
        var block = new LiveValues([knob.Key]);
        var port = (FakePort?)null;

        hub.Follow(patch, block);
        port = backend.Opened.Single();

        port.Send(Cc(21, 10));
        port.Send(Cc(21, 40));
        Read(block, knob).ShouldBe(0.5d, 1e-6);

        port.Send(Cc(21, 90));
        Read(block, knob).ShouldBe(90 / 127d, 1e-6);

        port.Send(Cc(21, 20));
        Read(block, knob).ShouldBe(20 / 127d, 1e-6);
    }

    [Fact]
    public void Jumping_takes_the_controller_at_once()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);
        var (patch, knob) = Patched(0.5f, new MidiBinding(Device, 0, 21));
        var block = new LiveValues([knob.Key]);

        hub.Follow(patch, block);
        backend.Opened.Single().Send(Cc(21, 0));

        Read(block, knob).ShouldBe(0d);
    }

    [Fact]
    public void Turning_a_knob_on_screen_writes_every_block()
    {
        using var midi = new MidiHub(NoMidiInput.Instance);
        var hub = new ControlHub(midi);
        var (patch, knob) = Patched();
        var picture = new LiveValues([knob.Key]);
        var sound = new LiveValues([knob.Key]);

        hub.Follow(patch, picture, sound);
        hub.Set(knob.Id, 0.9f);

        Read(picture, knob).ShouldBe(0.9d, 1e-6);
        Read(sound, knob).ShouldBe(0.9d, 1e-6);
    }

    [Fact]
    public async Task Learning_takes_the_first_controller_that_really_moves()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);

        hub.Follow(new Patch(), new LiveValues([]));

        var learning = hub.LearnAsync([Device], CancellationToken.None);
        var port = backend.Opened.Single();

        port.Send(Cc(7, 64));
        port.Send(Cc(7, 65));
        learning.IsCompleted.ShouldBeFalse();

        port.Send(Cc(21, 10));
        port.Send(Cc(21, 20));

        (await learning).ShouldBe(new MidiBinding(Device, 1, 21));
        port.IsOpen.ShouldBeFalse();
    }

    /// <summary>
    /// The binding says which channel the controller moved on, since a drum
    /// machine sends the same number from every track; whether to keep it is the
    /// window's call.
    /// </summary>
    [Fact]
    public async Task Learning_says_which_channel_the_controller_moved_on()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);

        hub.Follow(new Patch(), new LiveValues([]));

        var learning = hub.LearnAsync([Device], CancellationToken.None);
        var port = backend.Opened.Single();

        port.Send(Cc(74, 10, channel: 3));
        port.Send(Cc(74, 40, channel: 3));

        (await learning).ShouldBe(new MidiBinding(Device, 3, 74));
    }

    /// <summary>
    /// Walking a panel, the knob just learned is still under the hand that turned
    /// it, so its controller going on moving must not be taken for the next knob;
    /// the same number on another track is another knob and is.
    /// </summary>
    [Fact]
    public async Task Learning_does_not_take_the_controller_it_is_told_to_leave()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);

        hub.Follow(new Patch(), new LiveValues([]));

        var learning = hub.LearnAsync([Device], CancellationToken.None, except: new MidiBinding(Device, 3, 74));
        var port = backend.Opened.Single();

        port.Send(Cc(74, 40, channel: 3));
        port.Send(Cc(74, 80, channel: 3));
        learning.IsCompleted.ShouldBeFalse();

        port.Send(Cc(74, 10, channel: 4));
        port.Send(Cc(74, 40, channel: 4));

        (await learning).ShouldBe(new MidiBinding(Device, 4, 74));
    }

    [Fact]
    public async Task Learning_given_up_hands_back_nothing()
    {
        using var midi = new MidiHub(new FakeInput("Test Controller"));
        var hub = new ControlHub(midi);
        using var cancel = new CancellationTokenSource();

        var learning = hub.LearnAsync([Device], cancel.Token);
        await cancel.CancelAsync();

        (await learning).ShouldBeNull();
        hub.Learning.ShouldBeFalse();
    }

    /// <summary>
    /// Asking a second knob to learn while the first is still waiting leaves the
    /// second one listening.
    /// </summary>
    /// <remarks>
    /// "Learn MIDI controller" on knob A, then on knob B without Escape between.
    /// The second learn ends the first and opens the devices for itself, and the
    /// first's clean-up runs after that. Only the learn nothing has taken over
    /// from gives the devices back: on a patch learning its first controller
    /// none are bound, and giving them back would shut the port under the learn
    /// still waiting.
    /// </remarks>
    [Fact]
    public async Task A_second_learn_is_not_deafened_by_the_first_one_ending()
    {
        var backend = new FakeInput("Test Controller");
        using var midi = new MidiHub(backend);
        var hub = new ControlHub(midi);

        hub.Follow(new Patch(), new LiveValues([]));

        var first = hub.LearnAsync([Device], CancellationToken.None);
        var second = hub.LearnAsync([Device], CancellationToken.None);

        (await first).ShouldBeNull("the first learn gave way to the second");

        second.IsCompleted.ShouldBeFalse("the second is still waiting for a controller to move");

        var listening = backend.Opened.Where(port => port.IsOpen).ToList();

        listening.ShouldNotBeEmpty("so the device it is waiting on is still open");

        listening[^1].Send(Cc(21, 10));
        listening[^1].Send(Cc(21, 20));

        var learned = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        learned.ShouldBe(second, "and a controller moving is what it learns");
        (await second).ShouldBe(new MidiBinding(Device, 1, 21));
    }

    private sealed class FakeInput(params string[] names) : IMidiInput
    {
        public List<FakePort> Opened { get; } = [];

        public string Id => "fake";

        public string Name => "Stand-in";

        public int Priority => 1;

        public bool IsSupported => true;

        public IReadOnlyList<MidiPortInfo> Ports => MidiPorts.Named(names);

        public IMidiPort Open(string port, MidiCallback deliver)
        {
            var opened = new FakePort(port, deliver);

            Opened.Add(opened);

            return opened;
        }
    }

    private sealed class FakePort(string id, MidiCallback deliver) : IMidiPort
    {
        public string Id => id;

        public bool IsOpen { get; private set; } = true;

        public void Send(MidiMessage message) => deliver(message);

        public void Dispose() => IsOpen = false;
    }
}
