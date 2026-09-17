using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>
/// Putting a new version over an installed copy: what is replaced, what is left
/// alone, and that a failure halfway leaves the old copy as it was.
/// </summary>
public sealed class InstallerTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "flyback-install-" + Guid.NewGuid().ToString("N"));

    private string Payload => Path.Combine(scratch, "payload");

    private string Installed => Path.Combine(scratch, "installed");

    public void Dispose()
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }

    private static void Write(string root, string file, string text)
    {
        var path = Path.Combine(root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string file) => File.ReadAllText(Path.Combine(Installed, file));

    private bool Exists(string file) => File.Exists(Path.Combine(Installed, file));

    public InstallerTests()
    {
        Write(Installed, "Flyback.exe", "old shell");
        Write(Installed, "Flyback.Core.dll", "old core");
        Write(Installed, Path.Combine("plugins", "WinIO", "WinIO.dll"), "old plugin");
        Write(Installed, Path.Combine("plugins", "WinIO", "NAudio.Stale.dll"), "no longer shipped");
        Write(Installed, Path.Combine("plugins", "Mine", "Mine.dll"), "somebody's own plugin");
        Write(Installed, "notes.txt", "somebody's own file");

        Write(Payload, "Flyback.exe", "new shell");
        Write(Payload, "Flyback.Core.dll", "new core");
        Write(Payload, "Flyback.Gpu.dll", "new file");
        Write(Payload, Path.Combine("plugins", "WinIO", "WinIO.dll"), "new plugin");
    }

    [Fact]
    public void What_the_release_ships_replaces_what_was_there()
    {
        Installer.Install(Payload, Installed);

        Read("Flyback.exe").ShouldBe("new shell");
        Read("Flyback.Core.dll").ShouldBe("new core");
        Read("Flyback.Gpu.dll").ShouldBe("new file");
        Read(Path.Combine("plugins", "WinIO", "WinIO.dll")).ShouldBe("new plugin");

        Directory.Exists(Path.Combine(Installed, Installer.BackupName)).ShouldBeFalse();
    }

    /// <summary>
    /// A plugin is every assembly in its folder, so a shipped plugin's folder is
    /// replaced whole — and one the release does not ship is not the release's.
    /// </summary>
    [Fact]
    public void A_shipped_plugin_is_replaced_whole_and_anything_else_is_left_alone()
    {
        Installer.Install(Payload, Installed);

        Exists(Path.Combine("plugins", "WinIO", "NAudio.Stale.dll")).ShouldBeFalse();
        Read(Path.Combine("plugins", "Mine", "Mine.dll")).ShouldBe("somebody's own plugin");
        Read("notes.txt").ShouldBe("somebody's own file");
    }

    [Fact]
    public void A_failure_halfway_puts_the_old_copy_back()
    {
        Should.Throw<IOException>(() => Installer.Install(Payload, Installed, copying: file =>
        {
            if (file == "Flyback.Gpu.dll") throw new IOException("disk full");
        }));

        Read("Flyback.exe").ShouldBe("old shell");
        Read("Flyback.Core.dll").ShouldBe("old core");
        Exists("Flyback.Gpu.dll").ShouldBeFalse();
        Read(Path.Combine("plugins", "WinIO", "WinIO.dll")).ShouldBe("old plugin");
        Read(Path.Combine("plugins", "WinIO", "NAudio.Stale.dll")).ShouldBe("no longer shipped");
        Read(Path.Combine("plugins", "Mine", "Mine.dll")).ShouldBe("somebody's own plugin");

        Directory.Exists(Path.Combine(Installed, Installer.BackupName)).ShouldBeFalse();
    }

    /// <summary>An install cut off by the machine going down is put back first, and then done properly.</summary>
    [Fact]
    public void An_install_that_was_cut_off_is_put_back_before_the_next()
    {
        Write(Installed, Path.Combine(Installer.BackupName, "Flyback.Core.dll"), "old core");
        Write(Installed, "Flyback.Core.dll", "half-written");

        Installer.Install(Payload, Installed);

        Read("Flyback.Core.dll").ShouldBe("new core");
        Directory.Exists(Path.Combine(Installed, Installer.BackupName)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("plugins/WinIO/WinIO.dll", "plugins/WinIO")]
    [InlineData("Contents/MacOS/plugins/MacIO/sub/MacIO.dll", "Contents/MacOS/plugins/MacIO")]
    [InlineData("plugins/loose.dll", null)]
    [InlineData("Flyback.exe", null)]
    public void Which_plugin_folder_a_file_is_in(string file, string? folder)
    {
        Installer.PluginFolder(file.Replace('/', Path.DirectorySeparatorChar))
            .ShouldBe(folder?.Replace('/', Path.DirectorySeparatorChar));
    }
}
