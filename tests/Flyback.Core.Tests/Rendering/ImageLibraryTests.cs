using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// <see cref="ImageLibrary"/> — the folder a patch's pictures are actually read
/// from, as opposed to the stub the module tests hand the compiler.
/// </summary>
/// <remarks>
/// What this class is for is not decoding, which is <see cref="PngReader"/>'s,
/// but holding: every edit recompiles the whole patch, so a library that opened a
/// file would open it on every knob turn — and a patch naming a file that is not
/// there is recompiled just as often as one naming a file that is. So the tests
/// that matter are the ones that show a second look never reaches the disk, and
/// that the ways of emptying the cache empty exactly as much as they say.
/// </remarks>
public class ImageLibraryTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-pictures").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string Write(string name, byte red = 255)
    {
        var path = Path.Combine(folder, name);
        var bgra = new byte[4];

        bgra[0] = 0;
        bgra[1] = 0;
        bgra[2] = red;
        bgra[3] = 255;

        PngWriter.WriteBgra(path, bgra, 1, 1, 4);

        return path;
    }

    [Fact]
    public void A_picture_beside_the_patch_is_found_by_its_bare_name()
    {
        Write("moon.png");

        var library = new ImageLibrary { Beside = folder };

        library.Find("moon.png").ShouldNotBeNull();
        library.Count.ShouldBe(1);
    }

    /// <summary>
    /// The whole reason this type exists rather than a call to the reader:
    /// looking twice must not read twice. Shown by taking the file away, which
    /// nothing but a cache could survive.
    /// </summary>
    [Fact]
    public void A_picture_is_read_once_and_kept()
    {
        var path = Write("moon.png");
        var library = new ImageLibrary { Beside = folder };

        library.Find("moon.png").ShouldNotBeNull();

        File.Delete(path);

        library.Find("moon.png").ShouldNotBeNull();
    }

    /// <summary>
    /// A file that is not there is remembered as firmly as one that is — the
    /// recompile that follows every keystroke must not go back to the disk for a
    /// name that was missing a moment ago.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_there_is_remembered_as_missing_without_being_counted()
    {
        var library = new ImageLibrary { Beside = folder };

        library.Find("gone.png").ShouldBeNull();
        library.Explain("gone.png").ShouldBe("there is no file there.");

        // Held as an answer, but a cache of pictures holds no picture for it.
        library.Count.ShouldBe(0);
    }

    [Fact]
    public void A_file_that_is_not_a_picture_says_which_way_it_is_wrong()
    {
        File.WriteAllText(Path.Combine(folder, "notes.png"), "this is not a PNG");

        var library = new ImageLibrary { Beside = folder };

        library.Find("notes.png").ShouldBeNull();
        library.Explain("notes.png").ShouldBe("it is not a PNG.");
    }

    [Fact]
    public void A_path_that_names_nothing_at_all_is_missing_rather_than_a_fault()
    {
        var library = new ImageLibrary { Beside = folder };

        library.Find(string.Empty).ShouldBeNull();
        library.Find("   ").ShouldBeNull();
        library.Explain(string.Empty).ShouldBe("there is no file there.");
    }

    /// <summary>
    /// Forgetting one name drops that one and leaves the rest, which is what an
    /// editor does when a single file on disk has changed under it.
    /// </summary>
    [Fact]
    public void Forgetting_one_picture_leaves_the_others_held()
    {
        var moon = Write("moon.png");
        Write("sun.png");

        var library = new ImageLibrary { Beside = folder };

        library.Find("moon.png").ShouldNotBeNull();
        library.Find("sun.png").ShouldNotBeNull();

        File.Delete(moon);
        library.Forget("moon.png");

        library.Find("moon.png").ShouldBeNull();
        library.Find("sun.png").ShouldNotBeNull();
    }

    [Fact]
    public void Forgetting_everything_empties_the_whole_cache()
    {
        var moon = Write("moon.png");
        var library = new ImageLibrary { Beside = folder };

        library.Find("moon.png").ShouldNotBeNull();
        library.Count.ShouldBe(1);

        File.Delete(moon);
        library.Forget();

        library.Count.ShouldBe(0);
        library.Find("moon.png").ShouldBeNull();
    }

    /// <summary>
    /// Pointing the library at another folder invalidates everything in it,
    /// because the same bare name means a different file there.
    /// </summary>
    [Fact]
    public void Moving_the_folder_forgets_what_was_read_from_the_old_one()
    {
        Write("moon.png");
        var library = new ImageLibrary { Beside = folder };

        library.Find("moon.png").ShouldNotBeNull();

        var elsewhere = Directory.CreateTempSubdirectory("flyback-pictures-other").FullName;

        try
        {
            library.Beside = elsewhere;

            library.Count.ShouldBe(0);
            library.Find("moon.png").ShouldBeNull();
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>
    /// Setting the same folder again is not a move, and must not throw away a
    /// cache the patch is about to ask for — a save that rewrites the path to
    /// where it already was is an ordinary thing to do.
    /// </summary>
    [Fact]
    public void Setting_the_same_folder_again_keeps_what_was_read()
    {
        var path = Write("moon.png");
        var library = new ImageLibrary { Beside = folder };

        library.Find("moon.png").ShouldNotBeNull();

        File.Delete(path);
        library.Beside = folder;

        library.Find("moon.png").ShouldNotBeNull();
    }

    /// <summary>
    /// A rooted path says where it is and is taken at its word, so a patch may
    /// name a picture that lives nowhere near it.
    /// </summary>
    [Fact]
    public void A_rooted_path_is_read_from_where_it_says_rather_than_beside_the_patch()
    {
        var elsewhere = Directory.CreateTempSubdirectory("flyback-pictures-rooted").FullName;

        try
        {
            var path = Path.Combine(elsewhere, "moon.png");
            var bgra = new byte[] { 0, 0, 255, 255 };

            PngWriter.WriteBgra(path, bgra, 1, 1, 4);

            var library = new ImageLibrary { Beside = folder };

            library.Find(path).ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }
}
