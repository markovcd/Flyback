using System.Runtime.Versioning;
using Flyback.App.Files;
using Microsoft.Win32;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Files;

/// <summary>
/// The keys Windows reads, written under a scratch key of the current user's rather
/// than under <c>Software\Classes</c>, so no test run changes what opens a file.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileTypesTests : IDisposable
{
    private readonly string scratch = @"Software\Flyback.Tests\" + Guid.NewGuid().ToString("N");

    private readonly string folder = Directory.CreateTempSubdirectory("flyback-file-types-").FullName;

    private string Editor => Path.Combine(folder, "Flyback.exe");

    private string Viewer => Path.Combine(folder, "flyback-viewer.exe");

    public WindowsFileTypesTests()
    {
        File.WriteAllText(Editor, "");
        File.WriteAllText(Viewer, "");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(scratch, throwOnMissingSubKey: false);

        Directory.Delete(folder, recursive: true);
    }

    private (WindowsFileTypes Types, RegistryKey Classes, int[] Announced) Build()
    {
        var classes = Registry.CurrentUser.CreateSubKey(scratch);
        var announced = new int[1];

        return (new WindowsFileTypes(classes, Editor, Viewer, () => announced[0]++), classes, announced);
    }

    private static string? Default(RegistryKey classes, string key)
    {
        using var opened = classes.OpenSubKey(key);

        return opened?.GetValue("") as string;
    }

    [Fact]
    public void The_viewer_opens_all_three_kinds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry is Windows'");

        var (types, classes, announced) = Build();

        types.Apply(FileOpener.Viewer);

        foreach (var (extension, id) in new[] { (".fbk", "Flyback.Patch"), (".fbkb", "Flyback.Bundle"), (".fbks", "Flyback.Text") })
        {
            Default(classes, extension).ShouldBe(id);
            Default(classes, $@"{id}\shell\open\command").ShouldBe($"\"{Viewer}\" \"%1\"");
        }

        announced[0].ShouldBe(1);
    }

    [Fact]
    public void Choosing_the_editor_replaces_the_viewer()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry is Windows'");

        var (types, classes, _) = Build();

        types.Apply(FileOpener.Viewer);
        types.Apply(FileOpener.Editor);

        Default(classes, @"Flyback.Patch\shell\open\command").ShouldBe($"\"{Editor}\" \"%1\"");
    }

    [Fact]
    public void Nothing_takes_back_all_of_Flybacks_keys()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry is Windows'");

        var (types, classes, _) = Build();

        types.Apply(FileOpener.Editor);
        types.Apply(FileOpener.None);

        classes.GetSubKeyNames().ShouldBeEmpty();
    }

    /// <summary>Another program's claim on an extension survives Flyback letting go of it.</summary>
    [Fact]
    public void Nothing_leaves_another_programs_keys_alone()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry is Windows'");

        var (types, classes, _) = Build();

        types.Apply(FileOpener.Editor);

        using (var extension = classes.CreateSubKey(".fbk"))
        {
            extension.SetValue("", "Other.Program");

            using var others = extension.CreateSubKey("OpenWithProgids");
            others.SetValue("Other.Program", "");
        }

        types.Apply(FileOpener.None);

        Default(classes, ".fbk").ShouldBe("Other.Program");

        using var remaining = classes.OpenSubKey(@".fbk\OpenWithProgids");
        remaining.ShouldNotBeNull().GetValueNames().ShouldBe(["Other.Program"]);
    }

    [Fact]
    public void A_viewer_that_is_not_there_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry is Windows'");

        var (types, classes, _) = Build();

        File.Delete(Viewer);

        Should.Throw<FileNotFoundException>(() => types.Apply(FileOpener.Viewer));
        classes.GetSubKeyNames().ShouldBeEmpty();
    }
}
