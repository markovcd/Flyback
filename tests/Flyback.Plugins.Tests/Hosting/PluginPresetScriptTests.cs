using Flyback.Core.Tests.Compile;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Every plugin's preset as the JavaScript the page plays, under Node, against the
/// interpreter as <see cref="ScriptRun.ShouldMatch"/> compares them. Skipped where there is no Node.
/// </summary>
public class PluginPresetScriptTests
{
    public static TheoryData<string> Names => PluginPresetPrograms.Names;

    [Theory]
    [MemberData(nameof(Names))]
    public void The_sound_is_the_interpreters(string name)
    {
        Assert.SkipWhen(NodeJs.Path is null, "no Node on this machine");

        var program = PluginPresetPrograms.Compiled(name, video: false);

        ScriptRun.ShouldMatch(ScriptRun.Interpret(program, PluginPresetPrograms.Frames), ScriptRun.Script(program, PluginPresetPrograms.Frames), name, ScriptRun.HairFor(program));
    }
}
