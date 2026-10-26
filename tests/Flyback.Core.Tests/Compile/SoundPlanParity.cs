using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>A sound played with its fixed part lifted out, against the same sound played whole, to the bit.</summary>
internal static class SoundPlanParity
{
    public static void Sound(CompiledPatch program, int frames, string name)
    {
        var staged = new AudioRenderer();
        var whole = new AudioRenderer { Staged = false };

        var expected = new float[frames * 2];
        var actual = new float[frames * 2];

        // Two buffers each, so the second starts from what the first left in the bank.
        for (var half = 0; half < 2; half++)
        {
            whole.Render(program, expected.AsSpan(half * frames, frames));
            staged.Render(program, actual.AsSpan(half * frames, frames));
        }

        for (var i = 0; i < expected.Length; i++)
            BitConverter.SingleToInt32Bits(actual[i])
                .ShouldBe(BitConverter.SingleToInt32Bits(expected[i]), $"{name} at {i}: {actual[i]:R} against {expected[i]:R}");
    }
}
