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

    // --- the library folder ----------------------------------------------------

    [Fact]
    public void A_relative_path_not_beside_the_patch_is_found_in_the_library()
    {
        var (patch, library) = (Sub("patch"), Sub("library"));
        var shelved = Picture(Path.Combine(library, "stars", "moon.png"));

        PatchPaths.Resolve("stars/moon.png", patch, library).ShouldBe(shelved);
        PatchPaths.Carriable("stars/moon.png", patch, library).ShouldBe(File.ReadAllBytes(shelved));
    }

    [Fact]
    public void A_file_beside_the_patch_comes_before_the_library()
    {
        var (patch, library) = (Sub("patch"), Sub("library"));
        var beside = Picture(Path.Combine(patch, "moon.png"));
        Picture(Path.Combine(library, "moon.png"));

        PatchPaths.Resolve("moon.png", patch, library).ShouldBe(beside);
    }

    [Fact]
    public void A_file_in_neither_is_named_beside_the_patch()
    {
        var (patch, library) = (Sub("patch"), Sub("library"));

        PatchPaths.Resolve("moon.png", patch, library).ShouldBe(Path.Combine(patch, "moon.png"));
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData(@"..\secret.png")]
    public void The_library_is_never_climbed_out_of(string path)
    {
        var (patch, library) = (Sub(Path.Combine("a", "patch")), Sub("library"));
        Picture(Path.Combine(folder, "secret.png"));

        PatchPaths.Resolve(path, patch, library).ShouldBe(PatchPaths.Resolve(path, patch));
    }

    [Fact]
    public void An_absolute_path_is_not_looked_for_in_the_library()
    {
        var library = Sub("library");
        Picture(Path.Combine(library, "moon.png"));

        var gone = Path.Combine(Sub("elsewhere"), "moon.png");

        PatchPaths.Resolve(gone, beside: null, library).ShouldBe(gone);
    }

    [Fact]
    public void A_file_picked_inside_the_library_is_named_from_it()
    {
        var library = Sub("library");

        PatchPaths.Named(Path.Combine(library, "stars", "moon.png"), library).ShouldBe("stars/moon.png");
        PatchPaths.Named(Path.Combine(folder, "moon.png"), library).ShouldBe(Path.Combine(folder, "moon.png"));
        PatchPaths.Named(Path.Combine(library, "moon.png"), library: null).ShouldBe(Path.Combine(library, "moon.png"));
    }

    private string Sub(string name) => Directory.CreateDirectory(Path.Combine(folder, name)).FullName;

    private static string Picture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PngWriter.WriteBgra(path, [0, 0, 255, 255], 1, 1, 4);

        return path;
    }
}
