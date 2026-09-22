using Flyback.App.Files;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Files;

/// <summary>What a Linux desktop is given to know Flyback's files by, written into a scratch data folder.</summary>
public sealed class LinuxFileTypesTests : IDisposable
{
    private readonly string data = Directory.CreateTempSubdirectory("flyback-xdg-").FullName;

    private readonly List<string> refreshed = [];

    private string Editor => Path.Combine(data, "Flyback");

    private string Viewer => Path.Combine(data, "flyback-viewer");

    public LinuxFileTypesTests()
    {
        File.WriteAllText(Editor, "");
        File.WriteAllText(Viewer, "");
    }

    public void Dispose() => Directory.Delete(data, recursive: true);

    private LinuxFileTypes Build() => new(data, Editor, Viewer, (tool, _) => refreshed.Add(tool));

    [Fact]
    public void The_editor_is_named_by_its_whole_path()
    {
        var types = Build();

        types.Apply(FileOpener.Editor);

        var entry = File.ReadAllText(types.Entry);

        entry.ShouldContain($"Exec={LinuxFileTypes.Quote(Editor)} %f");
        entry.ShouldContain("MimeType=application/x-flyback-patch;application/x-flyback-bundle;application/x-flyback-source;");
        entry.ShouldContain("NoDisplay=false");
        File.ReadAllText(types.Package).ShouldContain("<glob pattern=\"*.fbkb\"/>");
        refreshed.ShouldBe(["update-mime-database", "update-desktop-database"]);
    }

    [Fact]
    public void The_viewer_opens_the_files_without_joining_the_menu()
    {
        var types = Build();

        types.Apply(FileOpener.Viewer);

        var entry = File.ReadAllText(types.Entry);

        entry.ShouldContain($"Exec={LinuxFileTypes.Quote(Viewer)} %f");
        entry.ShouldContain("NoDisplay=true");
    }

    [Fact]
    public void Nothing_removes_both_files()
    {
        var types = Build();

        types.Apply(FileOpener.Editor);
        types.Apply(FileOpener.None);

        File.Exists(types.Entry).ShouldBeFalse();
        File.Exists(types.Package).ShouldBeFalse();
    }

    [Fact]
    public void The_package_is_valid_xml()
    {
        var package = System.Xml.Linq.XDocument.Parse(LinuxFileTypes.MimePackage());

        package.Root!.Elements().Count().ShouldBe(4);
    }

    /// <summary>
    /// Quoting first, then desktop-entry string escaping: a dollar sign becomes a
    /// backslash before it, and that backslash is written twice.
    /// </summary>
    [Theory]
    [InlineData("/opt/flyback/Flyback", "\"/opt/flyback/Flyback\"")]
    [InlineData("/home/a b/Flyback", "\"/home/a b/Flyback\"")]
    [InlineData("/home/$me/Flyback", "\"/home/\\\\$me/Flyback\"")]
    [InlineData("/home/100%/Flyback", "\"/home/100%%/Flyback\"")]
    public void A_path_is_quoted_for_the_exec_line(string path, string written) =>
        LinuxFileTypes.Quote(path).ShouldBe(written);
}
