using System.Xml.Linq;
using Flyback.Editor.Shell;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Files;

/// <summary>
/// A Store copy registers its file types through its package manifest, not the registry
/// (ADR-0199), so the manifest names the same kinds, with the same icons, as every other copy.
/// </summary>
public sealed class StoreManifestTests
{
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    private static XDocument Manifest() =>
        XDocument.Load(Repository.Path("src", "Flyback.Editor.Desktop", "Store", "AppxManifest.xml"));

    [Fact]
    public void The_Store_package_opens_every_kind_of_file_Flyback_registers()
    {
        var declared = Manifest().Descendants(Uap + "FileTypeAssociation")
            .Select(a => (
                Extension: a.Descendants(Uap + "FileType").Single().Value,
                Name: a.Element(Uap + "DisplayName")!.Value,
                Logo: a.Element(Uap + "Logo")!.Value))
            .OrderBy(a => a.Extension, StringComparer.Ordinal);

        var registered = FileTypes.Kinds
            .Select(k => (k.Extension, k.Name, Logo: $@"{FileTypes.IconFolder}\{k.Icon}.png"))
            .OrderBy(k => k.Extension, StringComparer.Ordinal);

        declared.ShouldBe(registered);
    }

    [Fact]
    public void Every_picture_the_Store_package_names_is_in_the_repository()
    {
        var pictures = Manifest().Descendants()
            .SelectMany(e => e.Attributes().Select(a => a.Value).Append(e.HasElements ? "" : e.Value))
            .Where(v => v.EndsWith(".png", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        pictures.ShouldNotBeEmpty();

        foreach (var picture in pictures)
            File.Exists(Repository.Path(["src", "Flyback.Editor.Desktop", .. picture.Split('\\')])).ShouldBeTrue(picture);
    }
}
