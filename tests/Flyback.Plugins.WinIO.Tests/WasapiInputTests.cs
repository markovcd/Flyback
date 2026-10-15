using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Midi;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.WinIO.Tests;

/// <summary>
/// The WASAPI input's offer and how it hands a packet over, which every machine can answer;
/// only a microphone needs Windows.
/// </summary>
public sealed class WasapiInputTests
{
    [Fact]
    public void It_is_registered_and_answers_no_off_windows()
    {
        var registry = new Registry();
        new WinIOPlugin().Register(registry);

        var input = registry.Inputs.ShouldHaveSingleItem().ShouldBeOfType<WasapiAudioInput>();
        input.Id.ShouldBe("wasapi-capture");
        input.IsSupported.ShouldBe(OperatingSystem.IsWindows());

        if (OperatingSystem.IsWindows()) return;

        input.Form(SettingValues.None).ShouldBeEmpty();
        Should.Throw<PlatformNotSupportedException>(() => input.Create(AudioFormat.Default, SettingValues.None));
    }

    [Fact]
    public void A_packet_arrives_as_the_floats_it_holds()
    {
        float[] sent = [0.5f, -0.25f, 1f, 0f];
        var heard = new List<float>();

        WasapiPacket.Deliver(Bytes(sent), silent: false, new float[8], samples => heard.AddRange(samples.ToArray()));

        heard.ShouldBe(sent);
    }

    [Fact]
    public void A_silent_packet_arrives_as_zeros_however_long_it_is()
    {
        var heard = new List<float>();

        WasapiPacket.Deliver(Bytes(Enumerable.Repeat(0.75f, 10).ToArray()), silent: true, new float[4], samples => heard.AddRange(samples.ToArray()));

        heard.Count.ShouldBe(10);
        heard.ShouldAllBe(sample => sample == 0f);
    }

    [Fact]
    public void An_empty_packet_delivers_nothing()
    {
        var calls = 0;

        WasapiPacket.Deliver([], silent: false, new float[4], _ => calls++);
        WasapiPacket.Deliver([], silent: true, new float[4], _ => calls++);

        calls.ShouldBe(0);
    }

    private static byte[] Bytes(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Keeps the inputs a plugin registers and nothing else.</summary>
    private sealed class Registry : IPluginRegistry
    {
        public List<IAudioInput> Inputs { get; } = [];

        public void AddAudioInput(IAudioInput input) => Inputs.Add(input);

        public void AddAudioOutput(IAudioOutput output) { }

        public void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules) { }

        public void AddPresets(IReadOnlyList<PatchPreset> presets) { }

        public void AddPatchAssistant(IPatchAssistant assistant) { }

        public void AddSecretStore(ISecretStore store) { }

        public void AddMidiInput(IMidiInput input) { }

        public void AddDecisionModel(IDecisionModel model) { }
    }
}
