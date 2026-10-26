using Flyback.Core.Tests.Compile;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>Every plugin's preset played with its fixed part lifted out, against the same sound played whole, to the bit.</summary>
public class PluginPresetSoundPlanTests
{
    public static TheoryData<string> Names => PluginPresetPrograms.Names;

    [Theory]
    [MemberData(nameof(Names))]
    public void The_sound_is_the_same_with_its_fixed_part_lifted_out(string name) =>
        SoundPlanParity.Sound(PluginPresetPrograms.Compiled(name, video: false), PluginPresetPrograms.Frames, name);
}
