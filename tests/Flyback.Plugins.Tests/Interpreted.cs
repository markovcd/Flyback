using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Plugins.Tests;

/// <summary>A patch's sound, run sample by sample on the interpreter with the shipped plugins' modules.</summary>
internal static class Interpreted
{
    /// <summary>
    /// The speakers' program run for <paramref name="level"/>'s length, each sample arriving
    /// on x, and what came out of the Output's left.
    /// </summary>
    public static double[] Run(Patch patch, float[] level)
    {
        var result = patch.CompileForAudio(ShippedPlugins.Loaded.Modules);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var state = new DelayState(program, GlobalConstants.SampleRate);
        var registers = program.AllocateRegisters();
        var output = new double[level.Length];

        for (var i = 0; i < level.Length; i++)
        {
            program.Evaluate(level[i], 0f, i / (double)GlobalConstants.SampleRate, registers, default, state);
            output[i] = registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>The same, as the floats a speaker is handed.</summary>
    public static float[] Heard(Patch patch, float[] level) => [.. Run(patch, level).Select(sample => (float)sample)];
}
