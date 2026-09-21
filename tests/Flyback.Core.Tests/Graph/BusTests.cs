using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A Send and the Receives on its bus: a wire with no cable, compiled as the
/// wire it stands for.
/// </summary>
public class BusTests
{
    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    private const int Rate = 48_000;

    private static NodeInstance On(NodeInstance node, string bus)
    {
        node.SetState("bus", new JsonObject { ["bus"] = bus });
        return node;
    }

    private static NodeInstance Sink(PatchBuilder b) =>
        b.Add(NodeCatalog.OutputTypeId, 400, 0, (NodeCatalog.OutputVolumePort, 1f));

    /// <summary>What the speakers play over <paramref name="count"/> evaluations, with memory behind them.</summary>
    private static float[] Heard(Patch patch, int count = 1)
    {
        var result = patch.CompileForAudio(Modules);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();

        return
        [
            .. Enumerable.Range(0, count).Select(i =>
            {
                program.Evaluate(0d, 0d, i / (double)Rate, registers, default, state);
                return (float)registers[program.OutputBase];
            }),
        ];
    }

    private static IEnumerable<string> Warnings(Patch patch) =>
        patch.CompileForAudio(Modules).Issues
            .Where(i => i.Severity == IssueSeverity.Warning)
            .Select(i => i.Message);

    [Fact]
    public void A_receive_plays_what_its_send_carries()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.3f));
        var send = b.Add(NodeCatalog.SendTypeId);
        var receive = b.Add(NodeCatalog.ReceiveTypeId);
        var output = Sink(b);

        b.Wire(value, 0, send, 0);
        b.Wire(receive, 0, output, NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe([0.3f], 1e-6f);
        Warnings(b.Patch).ShouldBeEmpty("a fresh Send and a fresh Receive share a bus");
    }

    [Fact]
    public void A_bus_plays_exactly_what_the_wire_would()
    {
        var wired = new PatchBuilder(Modules);
        {
            var sine = wired.Add("osc.sine", (1, 440f));
            var half = wired.Add("math.mul", (1, 0.5f));
            wired.Wire(sine, 0, half, 0);
            wired.Wire(half, 0, Sink(wired), NodeCatalog.OutputLeftPort);
        }

        var bused = new PatchBuilder(Modules);
        {
            var sine = bused.Add("osc.sine", (1, 440f));
            var send = On(bused.Add(NodeCatalog.SendTypeId), "tone");
            var receive = On(bused.Add(NodeCatalog.ReceiveTypeId), "tone");
            var half = bused.Add("math.mul", (1, 0.5f));
            bused.Wire(sine, 0, send, 0);
            bused.Wire(receive, 0, half, 0);
            bused.Wire(half, 0, Sink(bused), NodeCatalog.OutputLeftPort);
        }

        Heard(bused.Patch, 256).ShouldBe(Heard(wired.Patch, 256));
    }

    [Fact]
    public void Any_number_of_receives_hear_one_send()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.25f));
        var send = On(b.Add(NodeCatalog.SendTypeId), "kick");
        var first = On(b.Add(NodeCatalog.ReceiveTypeId), "kick");
        var second = On(b.Add(NodeCatalog.ReceiveTypeId), "kick");
        var sum = b.Add("math.add");
        var output = Sink(b);

        b.Wire(value, 0, send, 0);
        b.Wire(first, 0, sum, 0);
        b.Wire(second, 0, sum, 1);
        b.Wire(sum, 0, output, NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe([0.5f], 1e-6f);
    }

    [Fact]
    public void A_bus_is_named_regardless_of_case_and_spacing()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.3f));
        var send = On(b.Add(NodeCatalog.SendTypeId), "Kick");
        var receive = On(b.Add(NodeCatalog.ReceiveTypeId), " kick ");

        b.Wire(value, 0, send, 0);
        b.Wire(receive, 0, Sink(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe([0.3f], 1e-6f);
    }

    /// <summary>Rests on its knob, as a socket with the wire pulled out does, and says why.</summary>
    [Fact]
    public void A_receive_with_no_send_carries_nothing_and_says_so()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.3f));
        var send = On(b.Add(NodeCatalog.SendTypeId), "kick");
        var receive = On(b.Add(NodeCatalog.ReceiveTypeId), "snare");
        var times = b.Add("math.mul", (1, 0.5f));

        b.Wire(value, 0, send, 0);
        b.Wire(value, 0, times, 0);
        b.Wire(receive, 0, times, 1);
        b.Wire(times, 0, Sink(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe([0.15f], 1e-6f);
        Warnings(b.Patch).ShouldContain(m => m.Contains("'snare'"));
    }

    [Fact]
    public void Two_sends_on_one_bus_are_one_too_many()
    {
        var b = new PatchBuilder(Modules);

        var low = b.Add("value", (0, 0.1f));
        var high = b.Add("value", (0, 0.9f));
        var first = On(b.Add(NodeCatalog.SendTypeId), "kick");
        var second = On(b.Add(NodeCatalog.SendTypeId), "kick");
        var receive = On(b.Add(NodeCatalog.ReceiveTypeId), "kick");

        b.Wire(low, 0, first, 0);
        b.Wire(high, 0, second, 0);
        b.Wire(receive, 0, Sink(b), NodeCatalog.OutputLeftPort);

        var heard = first.Id.CompareTo(second.Id) < 0 ? 0.1f : 0.9f;

        Heard(b.Patch).ShouldBe([heard], 1e-6f, "the Send with the lower id, which no edit moves");
        Warnings(b.Patch).ShouldContain(m => m.Contains("Another Send"));
    }

    /// <summary>A Receive fed back into its own Send is a loop, and carries the evaluation before.</summary>
    [Fact]
    public void A_loop_through_a_bus_is_delayed_like_any_other()
    {
        var b = new PatchBuilder(Modules);

        var receive = b.Add(NodeCatalog.ReceiveTypeId);
        var step = b.Add("math.add", (1, 0.25f));
        var send = b.Add(NodeCatalog.SendTypeId);

        b.Wire(receive, 0, step, 0);
        b.Wire(step, 0, send, 0);
        b.Wire(send, 0, Sink(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch, 3).ShouldBe([0.25f, 0.5f, 0.75f], 1e-6f);
    }

    [Fact]
    public void A_send_switched_off_puts_nothing_on_the_bus()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.3f));
        var send = b.Add(NodeCatalog.SendTypeId);
        var receive = b.Add(NodeCatalog.ReceiveTypeId);
        var times = b.Add("math.mul", (0, 0.5f), (1, 0.5f));

        b.Wire(value, 0, send, 0);
        b.Wire(receive, 0, times, 1);
        b.Wire(times, 0, Sink(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe([0.15f], 1e-6f);

        send.Off = true;

        Heard(b.Patch).ShouldBe([0.25f], 1e-6f, "the socket rests on its knob");
    }

    [Fact]
    public void The_bus_is_written_as_text_and_read_back()
    {
        var built = PatchLanguage.Build(
            """
            let kick = sine(freq: 60) |> send(bus: "kick")
            receive(bus: "kick") |> out.left
            """,
            Modules);

        built.Issues.ShouldBeEmpty();

        var printed = PatchPrinter.Print(built.Patch, Modules);
        printed.ShouldContain("bus: \"kick\"");

        var again = PatchLanguage.Build(printed, Modules);
        again.Issues.ShouldBeEmpty();
        Heard(again.Patch, 64).ShouldBe(Heard(built.Patch, 64));
    }
}
