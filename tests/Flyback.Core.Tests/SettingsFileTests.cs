using Shouldly;

namespace Flyback.Core.Tests;

/// <summary>The one settings file every program shares: each section read and written on its own.</summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-settings-file").FullName;

    private string Path => System.IO.Path.Combine(folder, "settings.json");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void A_section_reads_back_as_it_was_written()
    {
        SettingsFile.Write(Path, "output", """{ "width": 1280 }""");

        SettingsFile.Read(Path, "output").ShouldBe("""{"width":1280}""");
    }

    [Fact]
    public void Writing_one_section_keeps_the_others()
    {
        SettingsFile.Write(Path, "output", """{ "width": 1280 }""");
        SettingsFile.Write(Path, "canvas", """{ "compactModules": true }""");
        SettingsFile.Write(Path, "output", """{ "width": 640 }""");

        SettingsFile.Read(Path, "canvas").ShouldBe("""{"compactModules":true}""");
        SettingsFile.Read(Path, "output").ShouldBe("""{"width":640}""");
    }

    [Fact]
    public void No_file_and_no_section_both_read_as_nothing()
    {
        SettingsFile.Read(Path, "output").ShouldBeNull();

        SettingsFile.Write(Path, "canvas", "{}");

        SettingsFile.Read(Path, "output").ShouldBeNull();
    }

    /// <summary>A damaged file costs the settings it held, never a start, and the next save makes it whole.</summary>
    [Fact]
    public void A_file_that_is_not_json_reads_as_nothing_and_is_replaced_by_the_next_write()
    {
        File.WriteAllText(Path, "{ not json");

        SettingsFile.Read(Path, "output").ShouldBeNull();

        SettingsFile.Write(Path, "output", """{ "width": 1280 }""");

        SettingsFile.Read(Path, "output").ShouldBe("""{"width":1280}""");
    }

    [Fact]
    public void A_write_leaves_nothing_beside_the_file()
    {
        SettingsFile.Write(Path, "output", "{}");

        Directory.GetFiles(folder).ShouldBe([Path]);
    }
}
