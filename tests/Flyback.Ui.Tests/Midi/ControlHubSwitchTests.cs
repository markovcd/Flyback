using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Plugins.Midi;
using Flyback.Ui.Audio;
using Flyback.Ui.Midi;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Midi;

/// <summary>
/// Switches and buttons on the panel with a stand-in controller behind them: what a
/// controller's button or a pad does to each, and how a switch learns which kind of
/// button it has.
/// </summary>
public class ControlHubSwitchTests
{
    private const string Device = "midi:test-controller";

    private static ServiceProvider Services(IMidiInput backend) =>
        new ServiceCollection().AddTransport().AddSingleton(backend).BuildServiceProvider();

    private static MidiMessage Cc(int controller, int value) => new(MidiAction.Control, controller, value / 127f) { Channel = 1 };

    private static MidiMessage Down(int note) => new(MidiAction.Down, note, 0.8f) { Channel = 1 };

    private static MidiMessage Up(int note) => new(MidiAction.Up, note, 0f) { Channel = 1 };

    private static double Read(LiveValues block, PatchControl knob) => block.At(block.Keys.ToList().IndexOf(knob.Key));

    /// <summary>A hub following one control of <paramref name="kind"/> bound to <paramref name="binding"/>, and the port it hears.</summary>
    private static (ControlHub Hub, FakeMidiPort Port, LiveValues Block, PatchControl Control, ServiceProvider Services) Bound(
        ControlKind kind, MidiBinding binding)
    {
        var backend = new FakeMidiInput("Test Controller");
        var services = Services(backend);
        var hub = services.GetRequiredService<ControlHub>();
        var patch = new Patch();
        var control = patch.AddControl(value: 0f);
        control.Kind = kind;
        control.Midi = binding;
        var block = new LiveValues([control.Key]);

        hub.Follow(patch, block);

        return (hub, backend.Opened.Single(), block, control, services);
    }

    [Fact]
    public void A_switch_flips_at_each_press_of_its_button_and_not_at_the_release()
    {
        var (_, port, block, control, services) = Bound(ControlKind.Switch, new MidiBinding(Device, 0, 64));
        using var lifetime = services;

        port.Send(Cc(64, 127));
        Read(block, control).ShouldBe(1d);

        port.Send(Cc(64, 0));
        Read(block, control).ShouldBe(1d);

        port.Send(Cc(64, 127));
        port.Send(Cc(64, 126));
        Read(block, control).ShouldBe(0d);
    }

    [Fact]
    public void A_switch_on_a_latching_button_follows_it()
    {
        var (_, port, block, control, services) = Bound(ControlKind.Switch, new MidiBinding(Device, 0, 64) { Latching = true });
        using var lifetime = services;

        port.Send(Cc(64, 127));
        Read(block, control).ShouldBe(1d);

        port.Send(Cc(64, 0));
        Read(block, control).ShouldBe(0d);
    }

    [Fact]
    public void A_button_is_on_only_while_its_controller_button_is_down()
    {
        var (hub, port, block, control, services) = Bound(ControlKind.Button, new MidiBinding(Device, 0, 64));
        using var lifetime = services;
        var turned = new List<float>();
        hub.Turned += (_, value) => turned.Add(value);

        port.Send(Cc(64, 127));
        Read(block, control).ShouldBe(1d);

        port.Send(Cc(64, 0));
        Read(block, control).ShouldBe(0d);
        turned.ShouldBe([1f, 0f]);
    }

    [Fact]
    public void A_pad_flips_a_switch_at_each_strike()
    {
        var (_, port, block, control, services) = Bound(ControlKind.Switch, new MidiBinding(Device, 0, 36) { Note = true });
        using var lifetime = services;

        port.Send(Down(36));
        port.Send(Up(36));
        Read(block, control).ShouldBe(1d);

        port.Send(Down(36));
        Read(block, control).ShouldBe(0d);
    }

    [Fact]
    public void A_pad_holds_a_button_down_until_it_is_let_go()
    {
        var (_, port, block, control, services) = Bound(ControlKind.Button, new MidiBinding(Device, 0, 36) { Note = true });
        using var lifetime = services;

        port.Send(Down(36));
        Read(block, control).ShouldBe(1d);

        port.Send(Up(37));
        Read(block, control).ShouldBe(1d);

        port.Send(Up(36));
        Read(block, control).ShouldBe(0d);
    }

    [Fact]
    public void A_knob_hears_no_pad()
    {
        var (_, port, block, control, services) = Bound(ControlKind.Knob, new MidiBinding(Device, 0, 36) { Note = true });
        using var lifetime = services;

        port.Send(Down(36));

        Read(block, control).ShouldBe(0d);
    }

    [Fact]
    public async Task Learning_a_switch_takes_the_press_and_hears_the_release_of_a_momentary_button()
    {
        var backend = new FakeMidiInput("Test Controller");
        using var services = Services(backend);
        var hub = services.GetRequiredService<ControlHub>();
        hub.ReleaseWindow = TimeSpan.FromSeconds(30);

        var learning = hub.LearnAsync([Device], CancellationToken.None, kind: ControlKind.Switch);
        var port = backend.Opened.Single();

        port.Send(Cc(64, 127));
        port.Send(Cc(64, 0));

        (await learning).ShouldBe(new MidiBinding(Device, 1, 64));
    }

    [Fact]
    public async Task Learning_a_switch_on_a_button_that_sends_no_release_takes_it_as_latching()
    {
        var backend = new FakeMidiInput("Test Controller");
        using var services = Services(backend);
        var hub = services.GetRequiredService<ControlHub>();
        hub.ReleaseWindow = TimeSpan.FromMilliseconds(20);

        var learning = hub.LearnAsync([Device], CancellationToken.None, kind: ControlKind.Switch);
        backend.Opened.Single().Send(Cc(64, 127));

        (await learning).ShouldBe(new MidiBinding(Device, 1, 64) { Latching = true });
    }

    [Fact]
    public async Task Learning_a_button_takes_a_pad_struck()
    {
        var backend = new FakeMidiInput("Test Controller");
        using var services = Services(backend);
        var hub = services.GetRequiredService<ControlHub>();

        var learning = hub.LearnAsync([Device], CancellationToken.None, kind: ControlKind.Button);
        backend.Opened.Single().Send(Down(38));

        (await learning).ShouldBe(new MidiBinding(Device, 1, 38) { Note = true });
    }
}
