using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The presets the preset site starts with. They are files rather than code, so a
/// change to the file format or to a module one of them uses has to migrate them in
/// the same commit, and these are what fail until it does (ADR-0138).
/// </summary>
public class PresetSiteDefaultsTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "Defaults");

    public static TheoryData<string> Every =>
        [.. Directory.EnumerateFiles(Folder).Select(Path.GetFileName).OfType<string>()];

    private static LoadedBundle Open(string name)
    {
        var loaded = ShippedPlugins.Loaded;
        var path = Path.Combine(Folder, name);

        if (Path.GetExtension(name) == PatchBundle.Extension)
        {
            using var archive = File.OpenRead(path);
            return PatchBundle.Read(archive, loaded.Modules);
        }

        var load = PatchIO.Read(File.ReadAllText(path), loaded.Modules);
        return new LoadedBundle(load.Patch, new Dictionary<string, byte[]>(), Load: load);
    }

    [Fact]
    public void There_is_at_least_one() => Every.ShouldNotBeEmpty();

    [Theory]
    [MemberData(nameof(Every))]
    public void Each_is_written_in_the_current_format_and_opens_whole(string name)
    {
        var load = Open(name).Load!;

        load.Version.ShouldBe(PatchIO.FormatVersion, $"{name} is written in an older layout: migrate it");
        load.IsComplete.ShouldBeTrue(
            $"{name} names {string.Join(", ", [.. load.UnknownModules, .. load.MissingProviders.Select(p => p.Id)])}");
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Each_carries_every_file_it_names(string name)
    {
        var bundle = Open(name);

        foreach (var file in PatchBundle.Files(bundle.Patch, ShippedPlugins.Loaded.Modules))
            bundle.Files.ShouldContainKey(file, $"{name} names {file} without carrying it");
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Each_compiles_with_nothing_to_say(string name)
    {
        var modules = ShippedPlugins.Loaded.Modules;
        var bundle = Open(name);
        var files = BundleFiles.Of(bundle);

        foreach (var result in new[]
                 {
                     bundle.Patch.CompileForVideo(modules, files, files),
                     bundle.Patch.CompileForAudio(modules, files, files),
                 })
            result.Issues.Select(i => i.Message).ShouldBeEmpty(name);
    }
}
