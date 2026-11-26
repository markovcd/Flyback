using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Composition;
using Flyback.Editor.Packs;
using Flyback.Editor.Shell;
using Flyback.Engine.Packs;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.Editor.Tests.Packs;

/// <summary>A pack added to the packs folder while the editor runs joining its module list.</summary>
public sealed class PackShelfTests : EditorTest
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-shelf-");

    public override void Dispose()
    {
        base.Dispose();
        folder.Delete(recursive: true);
    }

    private const string Echoes = "pack ada.echoes 1.0.0\nmodule wobble(rate = 3) = sine(freq: rate)\n";

    private MainWindow Window() => Open(setup: new EditorSetup { Folders = new EditorFolders { PackFolder = folder.FullName } });

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(folder.FullName, name), text);

    [AvaloniaFact]
    public void A_pack_added_while_the_editor_runs_joins_the_module_list()
    {
        var window = Window();
        var plugins = Service<PluginCatalog>(window);

        Service<PackShelf>(window).Refresh().ShouldBeFalse("nothing has changed yet");

        Write("echoes.fbks", Echoes);

        Service<PackShelf>(window).Refresh().ShouldBeTrue();
        plugins.Modules.Get("ada.echoes.wobble").ShouldNotBeNull();
        Service<PackShelf>(window).Refresh().ShouldBeFalse("and it is read once");
    }

    [AvaloniaFact]
    public void A_pack_written_again_replaces_the_one_read()
    {
        Write("echoes.fbks", Echoes);

        var window = Window();
        var plugins = Service<PluginCatalog>(window);
        Service<PackShelf>(window).Refresh();

        Write("echoes.fbks", Echoes.Replace("1.0.0", "1.1.0") + "module hum(level = 0.1) = sine(freq: 50, amp: level)\n");
        File.SetLastWriteTimeUtc(Path.Combine(folder.FullName, "echoes.fbks"), DateTime.UtcNow.AddMinutes(1));

        Service<PackShelf>(window).Refresh().ShouldBeTrue();
        plugins.Modules.Get("ada.echoes.hum").ShouldNotBeNull();
        plugins.Modules.ProviderOf("ada.echoes.hum").ShouldBe(new("ada.echoes", "ada.echoes 1.1.0"));
    }

    [AvaloniaFact]
    public void A_pack_a_bundle_brought_stays_when_the_folder_is_read_again()
    {
        var window = Window();
        var plugins = Service<PluginCatalog>(window);

        var (brought, issues) = PackModules.Into(plugins.Modules, "pack ada.tones 1.0.0\nmodule hum(level = 0.1) = sine(freq: 50, amp: level)\n");
        issues.ShouldBeEmpty();
        plugins.Take(brought);

        Write("echoes.fbks", Echoes);
        Service<PackShelf>(window).Refresh().ShouldBeTrue();

        plugins.Modules.Get("ada.tones.hum").ShouldNotBeNull();
        plugins.Modules.Get("ada.echoes.wobble").ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void A_broken_pack_says_so_on_the_report_line()
    {
        var window = Window();

        Write("broken.fbks", "pack ada.broken 1.0.0\nmodule bad(in = 0) = sine(fre: in)\n");
        Service<PackShelf>(window).Refresh();

        Settle(window);
        All<Avalonia.Controls.TextBlock>(window)
            .ShouldContain(t => t.Text != null && t.Text.StartsWith("A pack in the packs folder has a problem: broken.fbks: line 2", StringComparison.Ordinal));
    }
}
