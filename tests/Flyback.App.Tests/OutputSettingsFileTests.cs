using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>
/// The file the settings window's Output section is kept in.
/// </summary>
/// <remarks>
/// Nothing here opens a window. What is worth pinning is that the file cannot
/// stop the program starting, and that what went in comes back out.
/// </remarks>
public class OutputSettingsFileTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-output-settings-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(folder, "output.json");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void No_file_is_the_defaults()
    {
        var settings = OutputSettings.Load(File);

        settings.Width.ShouldBe(960);
        settings.Height.ShouldBe(540);
        settings.Gpu.ShouldBeTrue();
        settings.Compiled.ShouldBeTrue();
    }

    [Fact]
    public void What_is_saved_comes_back()
    {
        new OutputSettings { Width = 320, Height = 180, Gpu = false, Compiled = false }.Save(File);

        var settings = OutputSettings.Load(File);

        settings.Width.ShouldBe(320);
        settings.Height.ShouldBe(180);
        settings.Gpu.ShouldBeFalse();
        settings.Compiled.ShouldBeFalse();
    }

    /// <summary>Losing a preference is not worth failing to start over.</summary>
    [Fact]
    public void A_file_that_is_not_json_is_the_defaults()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "{ not settings");

        OutputSettings.Load(File).Gpu.ShouldBeTrue();
    }
}
