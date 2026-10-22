using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>
/// Every shipped preset and plugin is tagged from one short list, so the gallery's and
/// the site's topics stay a handful a person can read at a glance.
/// </summary>
public class ShippedTagTests
{
    private static readonly string[] PresetTags =
        ["basics", "song", "playable", "drums", "melody", "ambient", "synthesis", "effects", "scope", "patterns"];

    private static readonly string[] PluginTags =
        ["sound", "picture", "midi", "effects", "ai", "secrets", "windows", "macos", "linux", "android"];

    /// <summary>The test fixtures laid out beside the shipped plugins.</summary>
    private static readonly string[] Fixtures = ["Sample", "FakeAssistant", "FakeDecider"];

    public static TheoryData<string> EveryPreset =>
        [.. ShippedPlugins.Loaded.Presets.Select(p => p.Name)];

    public static TheoryData<string> EveryPlugin =>
        [.. Directory.EnumerateDirectories(PluginHost.DefaultDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(folder => !Fixtures.Contains(folder))];

    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void Every_preset_is_tagged_from_the_list(string name)
    {
        var loaded = ShippedPlugins.Loaded;
        var tags = loaded.Presets.Single(p => p.Name == name).Build(loaded.Modules).Tags ?? [];

        tags.ShouldNotBeEmpty(name);
        tags.ShouldAllBe(tag => PresetTags.Contains(tag), name);
    }

    [Theory]
    [MemberData(nameof(EveryPlugin))]
    public void Every_plugin_is_tagged_from_the_list(string folder)
    {
        var description = PluginDescription.OfFolder(Path.Combine(PluginHost.DefaultDirectory, folder));

        description.ShouldNotBeNull(folder);
        description.Tags.ShouldNotBeEmpty(folder);
        description.Tags.ShouldAllBe(tag => PluginTags.Contains(tag), folder);
    }
}
