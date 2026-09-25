using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Mycelium's words: the recordings it carries, and where in the song they are heard.
/// Building and compiling are covered for every preset in <see cref="ShippedPresetTests"/>.
/// </summary>
public class MyceliumPresetTests
{
    /// <summary>Low, since what is checked is when the words start, not how they sound.</summary>
    private const int Rate = 2_000;

    private static readonly PluginCatalog Loaded = ShippedPlugins.Loaded;

    private static PatchPreset Preset => Loaded.Presets.Single(p => p.Name == "Mycelium");

    [Fact]
    public void It_carries_six_recordings()
    {
        Preset.Files.ShouldNotBeNull()().Keys.Order().ShouldBe(
            ["butterfly.wav", "one-side.wav", "sleepy-voice.wav", "so-many-sizes.wav", "who-are-you.wav", "who-i-was.wav"]);
    }

    /// <summary>
    /// Half a phrase in: nine and six tenths of a second at a hundred beats a minute.
    /// The same patch playing silent recordings is the song without its words.
    /// </summary>
    [Fact]
    public void The_first_line_starts_half_a_phrase_in()
    {
        var files = Preset.Files.ShouldNotBeNull()();
        var patch = Preset.Build(Loaded.Modules);

        var spoken = Play(patch, new BundleFiles(files), 9.5, 9.8);
        var unspoken = Play(patch, new BundleFiles(files.ToDictionary(f => f.Key, _ => Silence())), 9.5, 9.8);

        var first = Enumerable.Range(0, spoken.Length).First(i => spoken[i] != unspoken[i]);

        (9.5 + first / (double)Rate).ShouldBe(9.6, 0.002);
    }

    /// <summary>The left speaker from <paramref name="from"/> to <paramref name="to"/> seconds, from empty delay lines.</summary>
    private static double[] Play(Patch patch, ISampleLibrary samples, double from, double to)
    {
        var compiled = patch.CompileForAudio(Loaded.Modules, samples: samples);
        compiled.HasErrors.ShouldBeFalse(string.Join("; ", compiled.Issues.Select(i => i.Message)));

        var program = compiled.Program;
        var registers = program.AllocateRegisters();
        var state = new DelayState(program, Rate);
        var heard = new double[(int)((to - from) * Rate)];

        for (var i = 0; i < heard.Length; i++)
        {
            program.Evaluate(0, 0, from + i / (double)Rate, registers, default, state);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }

    /// <summary>A WAV of a hundred samples of nothing.</summary>
    private static byte[] Silence()
    {
        const int samples = 100;

        using var bytes = new MemoryStream();
        using var w = new BinaryWriter(bytes);

        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(22_050);
        w.Write(22_050 * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        w.Write(new byte[samples * 2]);
        w.Flush();

        return bytes.ToArray();
    }
}
