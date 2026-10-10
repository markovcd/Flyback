using Flyback.Core.Graph;
using Flyback.Core.Tests.Compile;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The presets the shipped plugins add, compiled as the app compiles them, for holding
/// each backend to the interpreter as Core.Tests holds the engine's presets.
/// </summary>
internal static class PluginPresetPrograms
{
    /// <summary>
    /// A quarter second of output frames: past a chorus's or a comb's delay, four times the
    /// engine presets' span, and a quarter of what a second would cost the gate.
    /// </summary>
    public const int Frames = ScriptRun.SampleRate / 4;

    public static TheoryData<string> Names =>
        [.. ShippedPlugins.Loaded.Presets.Where(p => !Presets.All.Any(e => e.Name == p.Name)).Select(p => p.Name)];

    public static CompiledPatch Compiled(string name, bool video)
    {
        var loaded = ShippedPlugins.Loaded;
        var preset = loaded.Presets.Single(p => p.Name == name);
        var patch = preset.Build(loaded.Modules);
        var carried = preset.Files is { } files ? new BundleFiles(files()) : null;

        var result = video
            ? patch.CompileForVideo(loaded.Modules, samples: carried, pictures: carried)
            : patch.CompileForAudio(loaded.Modules, samples: carried, pictures: carried);

        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        return result.Program;
    }
}
