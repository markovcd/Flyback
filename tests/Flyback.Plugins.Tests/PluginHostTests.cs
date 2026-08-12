using System.Runtime.Loader;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Loads the WASAPI plugin that this test project builds into its own output,
/// so what is exercised is a real separate assembly with real dependencies —
/// the part a stub plugin would not prove.
/// </summary>
public class PluginHostTests
{
    private static PluginCatalog Shipped() => PluginHost.Load();

    [Fact]
    public void The_shipped_plugin_is_found()
    {
        var catalog = Shipped();

        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Select(p => p.Info.Id).ShouldContain("wasapi");
    }

    [Fact]
    public void A_found_plugin_registers_what_it_offers()
    {
        var catalog = Shipped();

        catalog.AudioOutputs.Select(o => o.Id).ShouldContain("wasapi");
    }

    /// <summary>
    /// The failure this guards is silent: load the contract a second time inside
    /// the plugin's context and its types no longer match the host's, so a
    /// perfectly good plugin simply stops being one.
    /// </summary>
    [Fact]
    public void The_contract_keeps_one_identity_across_the_boundary()
    {
        var output = Shipped().AudioOutputs.Single(o => o.Id == "wasapi");

        // Loaded from somewhere other than the host's own context …
        var context = AssemblyLoadContext.GetLoadContext(output.GetType().Assembly);
        context.ShouldNotBe(AssemblyLoadContext.Default);

        // … yet the contract it implements is the very type this assembly compiled against.
        typeof(IAudioOutput).IsInstanceOfType(output).ShouldBeTrue();
        AssemblyLoadContext.GetLoadContext(typeof(IAudioOutput).Assembly)
            .ShouldBe(AssemblyLoadContext.Default);
    }

    /// <summary>
    /// The plugin loads everywhere; only the device is Windows-only. That split
    /// is the whole point of separating <see cref="IAudioOutput"/> from
    /// <see cref="IAudioDevice"/>, so it is worth pinning.
    /// </summary>
    [Fact]
    public void Support_is_answered_without_opening_a_device()
    {
        var output = Shipped().AudioOutputs.Single(o => o.Id == "wasapi");

        output.IsSupported.ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void A_missing_directory_is_not_an_error()
    {
        var catalog = PluginHost.Load(Path.Combine(Path.GetTempPath(), $"flyback-absent-{Guid.NewGuid():N}"));

        catalog.Plugins.ShouldBeEmpty();
        catalog.AudioOutputs.ShouldBeEmpty();
        catalog.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_directory_is_not_an_error()
    {
        using var folder = new TempFolder();

        PluginHost.Load(folder.Path).Plugins.ShouldBeEmpty();
    }

    /// <summary>
    /// A folder holding something that is not a managed assembly at all — a
    /// native library, or a half-written download. It must not stop the rest of
    /// the scan, and it must not throw.
    /// </summary>
    [Fact]
    public void Rubbish_in_a_plugin_folder_is_ignored()
    {
        using var folder = new TempFolder();

        Directory.CreateDirectory(Path.Combine(folder.Path, "Broken"));
        File.WriteAllBytes(Path.Combine(folder.Path, "Broken", "not-really.dll"), [0x00, 0x01, 0x02, 0x03]);

        var catalog = PluginHost.Load(folder.Path);

        catalog.Plugins.ShouldBeEmpty();
        catalog.AudioOutputs.ShouldBeEmpty();
    }

    /// <summary>
    /// A plugin folder with nothing in it at all is a common half-state — an
    /// uninstall that left the directory behind.
    /// </summary>
    [Fact]
    public void An_empty_plugin_folder_is_ignored()
    {
        using var folder = new TempFolder();

        Directory.CreateDirectory(Path.Combine(folder.Path, "Nothing"));

        PluginHost.Load(folder.Path).Problems.ShouldBeEmpty();
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } =
            Directory.CreateDirectory(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flyback-plugins-{Guid.NewGuid():N}")).FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
