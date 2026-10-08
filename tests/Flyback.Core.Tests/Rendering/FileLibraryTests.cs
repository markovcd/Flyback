using Flyback.Core.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// What a library hands an extra that reads a format of its own: a file's bytes, or a
/// picture decoded, read once and kept.
/// </summary>
public sealed class FileLibraryTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-files").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private ISampleLibrary Library() => new SampleLibrary { Beside = folder };

    private string Write(string name, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(folder, name), bytes);
        return name;
    }

    [Fact]
    public void A_file_is_read_once_and_again_once_forgotten()
    {
        var library = new SampleLibrary { Beside = folder };
        var name = Write("shape.svg", [1, 2, 3]);

        var first = ((ISampleLibrary)library).FindFile(name).ShouldNotBeNull();

        first.ShouldBe([1, 2, 3]);
        ((ISampleLibrary)library).FindFile(name).ShouldBeSameAs(first);

        library.Forget(name);
        ((ISampleLibrary)library).FindFile(name).ShouldNotBeSameAs(first);
    }

    [Fact]
    public void A_file_that_is_not_there_says_so()
    {
        var library = Library();

        library.FindFile("gone.svg").ShouldBeNull();
        library.ExplainFile("gone.svg").ShouldContain("no file there");
    }

    [Fact]
    public void A_file_past_the_cap_is_refused_before_it_is_read()
    {
        using (var huge = File.Create(Path.Combine(folder, "huge.obj")))
            huge.SetLength(SampleLibrary.MostFileBytes + 1L);

        var library = Library();

        library.FindFile("huge.obj").ShouldBeNull();
        library.ExplainFile("huge.obj").ShouldContain("larger than");
    }

    [Fact]
    public void A_picture_is_decoded_for_either_program_and_what_is_no_picture_says_why()
    {
        var bgra = new byte[2 * 2 * 4];
        Array.Fill(bgra, (byte)255);

        using var png = new MemoryStream();
        PngWriter.WriteBgra(png, bgra, 2, 2, 8);

        var library = Library();

        library.FindPicture(Write("white.png", png.ToArray())).ShouldNotBeNull().Width.ShouldBe(2);
        library.FindPicture(Write("text.png", [1, 2, 3])).ShouldBeNull();
        library.ExplainFile("text.png").ShouldContain("not a PNG");
    }

    [Fact]
    public void A_bundle_answers_for_the_files_it_carries_and_passes_the_rest_on()
    {
        var behind = new SampleLibrary { Beside = folder };
        Write("disk.obj", [9]);

        ISampleLibrary bundle = new BundleFiles(new Dictionary<string, byte[]> { ["held.svg"] = [7] }, behind);

        bundle.FindFile("held.svg").ShouldBe([7]);
        bundle.FindFile("disk.obj").ShouldBe([9]);
        bundle.FindFile("gone.svg").ShouldBeNull();
    }
}
