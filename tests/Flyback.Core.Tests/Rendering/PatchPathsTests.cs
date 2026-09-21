using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// <see cref="PatchPaths"/> — what a patch somebody else wrote may make Flyback
/// read, write and carry.
/// </summary>
public sealed class PatchPathsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-paths").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    // --- another machine -------------------------------------------------------

    [Theory]
    [InlineData(@"\\attacker\share\moon.png")]
    [InlineData("//attacker/share/moon.png")]
    [InlineData(@"\\?\UNC\attacker\share\moon.png")]
    [InlineData(@"\\.\pipe\moon")]
    public void A_path_to_another_machine_is_never_followed(string path)
    {
        PatchPaths.Resolve(path, folder).ShouldBeNull();
        PatchPaths.Resolve(path, beside: null).ShouldBeNull();
    }

    [Fact]
    public void A_patch_on_a_share_still_finds_the_files_beside_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a share is only a path on Windows");

        const string share = @"\\nas\music\patches";

        PatchPaths.Resolve("drums.wav", share).ShouldBe(@"\\nas\music\patches\drums.wav");
        PatchPaths.Resolve(@"\\nas\music\loops\drums.wav", share).ShouldBe(@"\\nas\music\loops\drums.wav");
        PatchPaths.Resolve(@"\\attacker\music\drums.wav", share).ShouldBeNull();
    }

    [Fact]
    public void A_library_says_why_it_did_not_look()
    {
        var pictures = new ImageLibrary { Beside = folder };
        var sounds = new SampleLibrary { Beside = folder };

        pictures.Find(@"\\attacker\share\moon.png").ShouldBeNull();
        pictures.Explain(@"\\attacker\share\moon.png").ShouldContain("another machine");
        sounds.Find(@"\\attacker\share\drums.wav").ShouldBeNull();
        sounds.Explain(@"\\attacker\share\drums.wav").ShouldContain("another machine");
    }

    [Fact]
    public void A_local_path_is_followed_as_it_always_was()
    {
        var absolute = Path.Combine(folder, "moon.png");

        PatchPaths.Resolve(absolute, beside: null).ShouldBe(absolute);
        PatchPaths.Resolve("moon.png", folder).ShouldBe(absolute);
    }

    // --- writing beside a patch ------------------------------------------------

    [Theory]
    [InlineData("../run.bat")]
    [InlineData(@"..\run.bat")]
    [InlineData("files/../../run.bat")]
    [InlineData(@"C:\Users\Public\run.bat")]
    [InlineData(@"\\attacker\share\run.bat")]
    public void Nothing_is_written_outside_the_patch_folder(string name) =>
        PatchPaths.Inside(folder, name).ShouldBeNull();

    [Theory]
    [InlineData("files/moon.png")]
    [InlineData(@"files\moon.png")]
    [InlineData("moon.png")]
    public void A_file_the_bundle_names_lands_beside_the_patch(string name) =>
        PatchPaths.Inside(folder, name).ShouldNotBeNull().ShouldStartWith(folder);

    // --- what a bundle carries -------------------------------------------------

    [Fact]
    public void A_bundle_carries_a_picture_and_a_sound()
    {
        var png = Path.Combine(folder, "moon.png");
        PngWriter.WriteBgra(png, [0, 0, 255, 255], 1, 1, 4);

        var wav = Path.Combine(folder, "drums.wav");
        File.WriteAllBytes(wav, [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8, 1, 2]);

        PatchPaths.Carriable("moon.png", folder).ShouldBe(File.ReadAllBytes(png));
        PatchPaths.Carriable(wav, beside: null).ShouldBe(File.ReadAllBytes(wav));
    }

    /// <summary>
    /// A patch naming <c>~/.ssh/id_rsa</c> as its picture would otherwise carry the
    /// key into a bundle the user then sends on.
    /// </summary>
    [Fact]
    public void A_bundle_never_carries_a_file_that_is_not_a_picture_or_a_sound()
    {
        var key = Path.Combine(folder, "id_rsa");
        File.WriteAllText(key, "-----BEGIN OPENSSH PRIVATE KEY-----");

        PatchPaths.Carriable(key, beside: null).ShouldBeNull();
        PatchPaths.Carriable("id_rsa", folder).ShouldBeNull();
    }
}
