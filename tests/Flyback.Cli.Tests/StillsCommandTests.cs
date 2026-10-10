using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using Flyback.Tests;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <c>flyback-cli stills</c>: what the index says of presets that draw nothing, where no
/// ffmpeg ever runs; the folder it is written into; and, with an ffmpeg on the machine, the
/// WebP a preset that draws gets.
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

    /// <summary>A picture: the x of each pixel as its color.</summary>
    private static Patch Drawn()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = builder.Add("coord", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0);
        builder.Wire(coord, 0, sink, NodeCatalog.OutputColorPort);

        return builder.Patch;
    }

    private (int Code, string Said, string Complaint) Run(DirectoryInfo folder, params PatchPreset[] presets) =>
        Run(folder, FakeFfmpeg, presets);

    private static (int Code, string Said, string Complaint) Run(DirectoryInfo folder, string ffmpeg, params PatchPreset[] presets)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var catalog = new PluginCatalog([], [], NodeCatalog.BuiltIn, presets, []);
        var plugins = new PluginRegistry(() => catalog, "nowhere", null);

        var code = StillsCommand.Run(plugins, folder, ffmpeg, output, error, CancellationToken.None).GetAwaiter().GetResult();

        return (code, output.ToString(), error.ToString());
    }

    private static StillIndex Index(string folder) =>
        StillIndex.Read(File.ReadAllText(Path.Combine(folder, StillIndex.FileName))).ShouldNotBeNull();

    private static StillEntry Entry(StillIndex index, string name) =>
        index.Of(name, PresetKind.Idea).ShouldNotBeNull();

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

        // The catalog orders its presets, so the order they are listed in is not this command's to promise.
        said.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
            ["Hum: sound only", "Idle: nothing to draw", "Broken: would not draw"], ignoreOrder: true);

        var index = Index(folder.FullName);
        index.Current.ShouldBeTrue();
        index.Presets.Count.ShouldBe(3);

        var (hum, idle, broken) = (Entry(index, "Hum"), Entry(index, "Idle"), Entry(index, "Broken"));
        hum.Still.ShouldBe(StillKind.SoundOnly);
        idle.Still.ShouldBe(StillKind.Nothing);
        broken.Still.ShouldBe(StillKind.Unavailable);
        hum.File.ShouldBeNull();
        idle.File.ShouldBeNull();
        broken.File.ShouldBeNull();
        hum.Heard.ShouldBeTrue();
        idle.Heard.ShouldBeFalse();
        folder.GetFiles().Select(f => f.Name).ShouldBe([StillIndex.FileName]);
    }

    /// <summary>With an ffmpeg to encode it, a preset that draws gets a WebP named in the index.</summary>
    [Fact]
    [TestCategory(TestCategory.Ffmpeg)]
    public void A_preset_that_draws_is_written_as_webp_and_indexed()
    {
        var ffmpeg = Ffmpeg.Resolve(null);
        TestCategory.Ffmpeg.Require(ffmpeg is not null, "no ffmpeg on this machine");

        var folder = new DirectoryInfo(Path.Combine(root.FullName, "drawn"));

        var (code, said, complaint) = Run(folder, ffmpeg!, new PatchPreset("Ramp", _ => Drawn()));

        code.ShouldBe(Exit.Ok, complaint);
        said.Trim().ShouldBe("Ramp: drawn");

        var entry = Entry(Index(folder.FullName), "Ramp");
        entry.Still.ShouldBe(StillKind.Picture);

        var bytes = File.ReadAllBytes(Path.Combine(folder.FullName, entry.File.ShouldNotBeNull()));
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("RIFF");
        System.Text.Encoding.ASCII.GetString(bytes, 8, 4).ShouldBe("WEBP");
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
