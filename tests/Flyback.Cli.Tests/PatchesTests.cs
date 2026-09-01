using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <see cref="Patches"/> — the one place a patch is opened, whether the path
/// named a loose file with its recordings beside it or a bundle carrying them.
/// </summary>
/// <remarks>
/// Nothing downstream of this knows there are two kinds of file, so what has to
/// hold is that both arrive at the same shape of answer and that neither can
/// reach a caller as an exception. Every way of failing is one sentence naming
/// the file, because the audience is somebody who typed a path and a script that
/// has to branch on an exit code.
/// </remarks>
public class PatchesTests
{
    private static Patch Plasma() =>
        Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

    private static (Opened? Opened, string Error) Open(FileInfo file)
    {
        var error = new StringWriter();
        return (Patches.Open(file, error), error.ToString());
    }

    private static (Patch? Patch, string Error) Read(FileInfo file)
    {
        var error = new StringWriter();
        return (Patches.Read(file, error), error.ToString());
    }

    /// <summary>
    /// Which of the two paths a file takes is decided by its extension alone,
    /// and a path typed by hand is as likely to shout it as not.
    /// </summary>
    [Fact]
    public void A_bundle_is_told_from_a_patch_by_extension_whatever_its_case()
    {
        Patches.Bundled(new FileInfo("song" + PatchBundle.Extension)).ShouldBeTrue();
        Patches.Bundled(new FileInfo("song" + PatchBundle.Extension.ToUpperInvariant())).ShouldBeTrue();
        Patches.Bundled(new FileInfo("song.fbk")).ShouldBeFalse();
        Patches.Bundled(new FileInfo("song")).ShouldBeFalse();
    }

    [Fact]
    public void A_path_naming_nothing_is_refused_by_name()
    {
        using var scratch = new Scratch();

        var (patch, error) = Read(scratch.File("nonesuch.fbk"));

        patch.ShouldBeNull();
        error.ShouldContain("no such file");
    }

    /// <summary>
    /// A file that is not a patch at all is the commonest way to point this at
    /// the wrong path, and it has to be the same one sentence rather than a
    /// stack trace out of the deserialiser.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_a_patch_is_refused_rather_than_thrown_from()
    {
        using var scratch = new Scratch();
        var file = scratch.File("notes.fbk");

        File.WriteAllText(file.FullName, "this is not a patch");

        var (patch, error) = Read(file);

        patch.ShouldBeNull();
        error.ShouldContain(file.Name);
    }

    /// <summary>
    /// Nothing read out of a file from a newer build can be trusted to mean what
    /// it says, so this is the one incomplete load that is refused outright
    /// rather than handed back for 'check' to describe.
    /// </summary>
    [Fact]
    public void A_patch_from_a_newer_build_is_refused()
    {
        using var scratch = new Scratch();
        var file = scratch.File("future.fbk");

        File.WriteAllText(file.FullName, """{ "Version": 999 }""");

        var (patch, error) = Read(file);

        patch.ShouldBeNull();
        error.ShouldContain(file.Name);
    }

    [Fact]
    public void A_patch_that_is_whole_is_read_without_complaint()
    {
        using var scratch = new Scratch();
        var file = scratch.File("plasma.fbk");

        File.WriteAllText(file.FullName, PatchIO.ToJson(Plasma()));

        var (patch, error) = Read(file);

        patch.ShouldNotBeNull();
        error.ShouldBeEmpty();
    }

    /// <summary>
    /// A patch short of one module still compiles, still renders, and still
    /// tells you more about itself than a refusal would — which is the whole
    /// reason 'check' can be pointed at a file like this.
    /// </summary>
    [Fact]
    public void A_patch_missing_a_module_is_complained_about_and_handed_back_anyway()
    {
        using var scratch = new Scratch();
        var file = scratch.File("partial.fbk");

        var patch = Plasma();
        patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = "osc.nonesuch" });

        File.WriteAllText(file.FullName, PatchIO.ToJson(patch));

        var (read, error) = Read(file);

        read.ShouldNotBeNull();
        error.ShouldContain("did not load completely");
    }

    /// <summary>
    /// The loose path roots both libraries at the folder holding the patch,
    /// which is what makes a recording named beside it findable at all.
    /// </summary>
    [Fact]
    public void A_loose_patch_looks_for_its_files_beside_itself()
    {
        using var scratch = new Scratch();
        var file = scratch.File("plasma.fbk");

        File.WriteAllText(file.FullName, PatchIO.ToJson(Plasma()));

        var (opened, error) = Open(file);

        opened.ShouldNotBeNull();
        error.ShouldBeEmpty();
        opened.Value.Samples.ShouldBeOfType<SampleLibrary>().Beside.ShouldBe(file.DirectoryName);
        opened.Value.Pictures.ShouldBeOfType<ImageLibrary>().Beside.ShouldBe(file.DirectoryName);
    }

    [Fact]
    public void A_loose_patch_that_cannot_be_read_opens_as_nothing()
    {
        using var scratch = new Scratch();

        var (opened, error) = Open(scratch.File("nonesuch.fbk"));

        opened.ShouldBeNull();
        error.ShouldContain("no such file");
    }

    /// <summary>
    /// The case the bundle format exists for: a build server holding one file
    /// opens a patch whose files it has never seen, and unpacks nothing to do it.
    /// </summary>
    [Fact]
    public void A_bundle_opens_without_being_unpacked()
    {
        using var scratch = new Scratch();
        var file = scratch.File("song" + PatchBundle.Extension);

        using (var archive = File.Create(file.FullName))
            PatchBundle.Write(archive, Plasma(), _ => null);

        var (opened, error) = Open(file);

        opened.ShouldNotBeNull();
        error.ShouldBeEmpty();
        opened.Value.Patch.Nodes.ShouldNotBeEmpty();

        Directory.GetFiles(file.DirectoryName!).ShouldHaveSingleItem();
    }

    /// <summary>
    /// A damaged bundle fails inside a zip reader rather than a patch reader,
    /// and that difference must not reach whoever typed the path.
    /// </summary>
    [Fact]
    public void A_bundle_that_is_damaged_is_refused_rather_than_thrown_from()
    {
        using var scratch = new Scratch();
        var file = scratch.File("damaged" + PatchBundle.Extension);

        File.WriteAllBytes(file.FullName, [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF]);

        var (opened, error) = Open(file);

        opened.ShouldBeNull();
        error.ShouldContain(file.Name);
    }

    /// <summary>A directory of its own per test, taken away afterwards.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly string path = Directory.CreateTempSubdirectory("flyback-patches").FullName;

        public FileInfo File(string name) => new(Path.Combine(path, name));

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
