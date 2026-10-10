using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <c>flyback-cli stills</c> over presets that draw nothing, so no ffmpeg ever runs: what the
/// index says of each, and the folder it is written into.
/// </summary>
public sealed class StillsCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("flyback-cli-stills");

    public void Dispose() => root.Delete(recursive: true);

    /// <summary>Stands in for ffmpeg, which is only looked for and never run.</summary>
    private string FakeFfmpeg
    {
        get
        {
            var path = Path.Combine(root.FullName, "ffmpeg");
            File.WriteAllText(path, "");

            return path;
        }
    }

    private static Patch Sound()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var tone = builder.Add(NodeCatalog.SineTypeId, 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(tone, 0, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    private static Patch Silent()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        return builder.Patch;
    }

    private (int Code, string Said, string Complaint) Run(DirectoryInfo folder, params PatchPreset[] presets)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var catalog = new PluginCatalog([], [], NodeCatalog.BuiltIn, presets, []);
        var plugins = new PluginRegistry(() => catalog, "nowhere", null);

        var code = StillsCommand.Run(plugins, folder, FakeFfmpeg, output, error, CancellationToken.None).GetAwaiter().GetResult();

        return (code, output.ToString(), error.ToString());
    }

    private StillIndex Index(string folder) =>
        StillIndex.Read(File.ReadAllText(Path.Combine(folder, StillIndex.FileName))).ShouldNotBeNull();

    [Fact]
    public void A_preset_with_no_picture_is_listed_with_why_and_no_file()
    {
        var folder = new DirectoryInfo(Path.Combine(root.FullName, "stills"));

        var (code, said, complaint) = Run(
            folder,
            new PatchPreset("Hum", _ => Sound()),
            new PatchPreset("Idle", _ => Silent()),
            new PatchPreset("Broken", _ => throw new InvalidOperationException("no build")));

        code.ShouldBe(Exit.Ok, complaint);
        said.ShouldBe($"Hum: sound only{Environment.NewLine}Idle: nothing to draw{Environment.NewLine}Broken: would not draw{Environment.NewLine}");

        var index = Index(folder.FullName);
        index.Current.ShouldBeTrue();
        index.Presets.Select(p => (p.Name, p.Still, p.File)).ShouldBe(
        [
            ("Hum", StillKind.SoundOnly, null),
            ("Idle", StillKind.Nothing, null),
            ("Broken", StillKind.Unavailable, null),
        ]);
        index.Presets[0].Heard.ShouldBeTrue();
        index.Presets[1].Heard.ShouldBeFalse();
        folder.GetFiles().Select(f => f.Name).ShouldBe([StillIndex.FileName]);
    }

    [Fact]
    public void The_folder_is_made_where_it_is_not_there()
    {
        var folder = new DirectoryInfo(Path.Combine(root.FullName, "site", "stills"));

        Run(folder, new PatchPreset("Hum", _ => Sound())).Code.ShouldBe(Exit.Ok);

        File.Exists(Path.Combine(folder.FullName, StillIndex.FileName)).ShouldBeTrue();
    }

    [Fact]
    public void A_folder_that_is_a_file_is_refused_naming_it()
    {
        var blocked = Path.Combine(root.FullName, "stills");
        File.WriteAllText(blocked, "");

        var (code, _, complaint) = Run(new DirectoryInfo(blocked), new PatchPreset("Hum", _ => Sound()));

        code.ShouldBe(Exit.Failed);
        complaint.ShouldContain(blocked);
    }
}
