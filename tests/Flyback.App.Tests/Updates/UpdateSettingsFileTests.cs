using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>The file the settings window's Updates section is kept in.</summary>
public sealed class UpdateSettingsFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-update-settings-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(folder, "update.json");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Updates_are_on_unless_switched_off()
    {
        UpdateSettings.Load(File).CheckForUpdates.ShouldBeTrue();
    }

    [Fact]
    public void Switched_off_stays_off()
    {
        new UpdateSettings { CheckForUpdates = false }.Save(File);

        UpdateSettings.Load(File).CheckForUpdates.ShouldBeFalse();
    }

    [Fact]
    public void An_unreadable_file_is_the_defaults()
    {
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "{ not json");

        UpdateSettings.Load(File).CheckForUpdates.ShouldBeTrue();
    }
}
