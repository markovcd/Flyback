using Flyback.Core.Tests.Compile;
using Flyback.Engine.Render;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>Every plugin's preset as the IL the editor plays, against the interpreter to the bit.</summary>
/// <remarks>
/// A delay line, an accumulator or a cell numbered differently shows only on a shape that
/// has one, and the plugin presets are the larger shapes.
/// </remarks>
public class PluginPresetIlTests
{
    public static TheoryData<string> Names => PluginPresetPrograms.Names;

    [Theory]
    [MemberData(nameof(Names))]
    public void The_picture_is_the_interpreters_to_the_bit(string name) =>
        IlParity.Picture(PluginPresetPrograms.Compiled(name, video: true), name);

    [Theory]
    [MemberData(nameof(Names))]
    public void The_sound_is_the_interpreters_to_the_bit(string name) =>
        IlParity.Sound(PluginPresetPrograms.Compiled(name, video: false), PluginPresetPrograms.Frames * new AudioRenderer().Oversample, name);
}
