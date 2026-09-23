using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Plugins.Hosting;
using Flyback.App.Tests.PluginPackages;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>A <c>.fbkp</c> dropped on the window, which asks before it installs anything.</summary>
public sealed class PluginInstallTests : UiTest
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-install-" + Guid.NewGuid().ToString("N"));

    private string Plugins => Path.Combine(folder, "plugins");

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private MainWindow Open(Action<Reopen?>? relaunch = null)
    {
        var window = Owned(new MainWindow(pluginFolder: Plugins, relaunch: relaunch));

        window.Show();
        Settle(window);

        return window;
    }

    private string Write(byte[] package)
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "ripple.fbkp");
        File.WriteAllBytes(path, package);

        return path;
    }

    private static ModalOverlay Dropped(MainWindow window, string path)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(RealStorageFile(path)));

        window.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, window, default, KeyModifiers.None));

        Pump(() => All<ModalOverlay>(window).Any());
        Settle(window);

        return All<ModalOverlay>(window).ShouldHaveSingleItem();
    }

    /// <summary>The platform's own file-backed <see cref="IStorageFile"/>; see <c>FileDropTests</c>.</summary>
    private static IStorageFile RealStorageFile(string path)
    {
        var type = typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile", throwOnError: true)!;
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];

        return (IStorageFile)ctor.Invoke([new FileInfo(path)]);
    }

    /// <summary>Opening the package crosses a disk read that finishes on the thread pool.</summary>
    private static void Pump(Func<bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);

        while (!until() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static Button Named(Control dialog, string name) => All<Button>(dialog).Single(b => b.Name == name);

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<string?> Texts(Control dialog) => All<SelectableTextBlock>(dialog).Select(t => t.Text);

    [AvaloniaFact]
    public void A_package_shows_what_it_says_it_is_and_installs_only_when_asked()
    {
        var window = Open();
        var dialog = Dropped(window, Write(Packages.For("win", "osx", "linux")));

        Texts(dialog).ShouldContain(Packages.Name);
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginAdds").Text.ShouldBe("modules, presets");
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginReaches").Text.ShouldBe("nothing outside Flyback that it names");
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginContract").Text!.ShouldContain("Flyback.Plugins ");
        All<Border>(dialog).ShouldContain(b => b.Name == "pluginWarning");
        Named(dialog, "install").IsEnabled.ShouldBeTrue();
        Directory.Exists(Plugins).ShouldBeFalse("nothing is written before Install is pressed");

        Press(Named(dialog, "install"));
        Pump(() => !All<ModalOverlay>(window).Any());

        Directory.Exists(Path.Combine(Plugins, PluginInstaller.PendingName, Packages.Folder)).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_package_shows_the_plugins_preview_author_tags_and_modules()
    {
        var window = Open();
        var dialog = Dropped(window, Write(Packages.ForSample()));

        Texts(dialog).ShouldContain("Sample modules");
        Texts(dialog).ShouldContain(t => t != null && t.EndsWith(", by Flyback", StringComparison.Ordinal));
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginTags").Text.ShouldBe("example, ripple, test-fixture");
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginModules").Text.ShouldBe("Ripple, Halve");

        var preview = All<Image>(dialog).Single(i => i.Name == "pluginPreview");

        preview.Source.ShouldNotBeNull().Size.Width.ShouldBe(160);
    }

    [AvaloniaFact]
    public void A_package_without_tags_or_a_preview_shows_neither()
    {
        var window = Open();
        var dialog = Dropped(window, Write(Packages.ForBare()));

        All<SelectableTextBlock>(dialog).ShouldNotContain(t => t.Name == "pluginTags");
        All<Image>(dialog).ShouldNotContain(i => i.Name == "pluginPreview");
    }

    [AvaloniaFact]
    public void Installing_with_restart_ticked_starts_Flyback_again_and_closes_this_window()
    {
        var relaunched = 0;
        var window = Open(_ => relaunched++);
        var dialog = Dropped(window, Write(Packages.For("win", "osx", "linux")));

        All<CheckBox>(dialog).Single(c => c.Name == "restart").IsChecked.ShouldBe(true, "a plugin does nothing until Flyback starts again");

        Press(Named(dialog, "install"));
        Pump(() => relaunched > 0);

        relaunched.ShouldBe(1);
        window.IsVisible.ShouldBeFalse();
        Directory.Exists(Path.Combine(Plugins, PluginInstaller.PendingName, Packages.Folder)).ShouldBeTrue();
    }

    /// <summary>
    /// Restarting before the last of them lands back on the same refusal, so the offer is
    /// off until there is nothing left to install.
    /// </summary>
    [AvaloniaFact]
    public void A_patch_still_short_of_others_does_not_offer_the_restart_yet()
    {
        var package = PluginPackage.Read(Packages.For("win", "osx", "linux"));

        // What the window itself does when the shell says there is no restart to offer yet.
        var awaiting = Show(PluginInstallView.View(
            package, "win", refusal: null, replacing: null, PluginChange.Install, offerRestart: false, awaiting: 2));

        All<CheckBox>(awaiting).ShouldNotContain(c => c.Name == "restart", "no restart to tick while others are still to install");
        All<TextBlock>(awaiting).ShouldContain(t => t.Name == "pluginAwaiting" && t.Text!.Contains("2 more plugins"));

        var last = Show(PluginInstallView.View(
            package, "win", refusal: null, replacing: null, PluginChange.Install, offerRestart: true));

        All<CheckBox>(last).Single(c => c.Name == "restart").IsChecked.ShouldBe(true);
        All<TextBlock>(last).ShouldNotContain(t => t.Name == "pluginAwaiting");
    }

    [AvaloniaFact]
    public void Installing_with_restart_unticked_leaves_the_window_open()
    {
        var relaunched = 0;
        var window = Open(_ => relaunched++);
        var dialog = Dropped(window, Write(Packages.For("win", "osx", "linux")));

        All<CheckBox>(dialog).Single(c => c.Name == "restart").IsChecked = false;
        Press(Named(dialog, "install"));
        Pump(() => !All<ModalOverlay>(window).Any());

        relaunched.ShouldBe(0);
        window.IsVisible.ShouldBeTrue();
        All<ReportLine>(window).Single().History.ShouldContain(h => h.EndsWith("loads the next time Flyback starts."));
    }

    [AvaloniaFact]
    public void A_window_that_cannot_restart_offers_no_restart()
    {
        var dialog = Dropped(Open(), Write(Packages.For("win", "osx", "linux")));

        All<CheckBox>(dialog).ShouldNotContain(c => c.Name == "restart");
    }

    [AvaloniaFact]
    public void Canceling_installs_nothing()
    {
        var window = Open();
        var dialog = Dropped(window, Write(Packages.For("win", "osx", "linux")));

        Press(Named(dialog, "cancel"));
        Pump(() => !All<ModalOverlay>(window).Any());

        Directory.Exists(Path.Combine(Plugins, PluginInstaller.PendingName)).ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_package_with_no_build_for_this_system_cannot_be_installed_and_says_why()
    {
        var elsewhere = PluginPackage.Platforms.First(p => p != PluginPackage.ThisPlatform);
        var window = Open();
        var dialog = Dropped(window, Write(Packages.For(elsewhere)));

        Named(dialog, "install").IsEnabled.ShouldBeFalse();
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginRefusal").Text
            .ShouldBe($"Cannot be installed. It has no build for {PluginPackage.Describe(PluginPackage.ThisPlatform)}, only for {PluginPackage.Describe(elsewhere)}.");
    }

    [AvaloniaFact]
    public void A_package_that_is_not_one_is_refused_without_a_dialog()
    {
        var window = Open();

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(RealStorageFile(Write(Packages.With("../evil.dll")))));
        window.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, window, default, KeyModifiers.None));

        Pump(() => All<ReportLine>(window).Single().History.Any(h => h.Contains("ripple.fbkp")));

        All<ModalOverlay>(window).ShouldBeEmpty();
        All<ReportLine>(window).Single().History.ShouldContain(h => h.StartsWith("ripple.fbkp was not installed. It holds a file named"));
    }

    [AvaloniaFact]
    public void A_package_says_which_key_signed_it()
    {
        var dialog = Dropped(Open(), Write(Packages.For("win", "osx", "linux")));

        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginSigner").Text
            .ShouldBe($"key {PackageSigner.Of(Packages.Key).Fingerprint}");
    }

    [AvaloniaFact]
    public void A_newer_build_of_an_installed_plugin_is_offered_as_an_update()
    {
        var older = PluginPackage.Read(Packages.Sign(Packages.Unsigned(Packages.Older, "win", "osx", "linux")));

        new PluginInstaller(Plugins, [], checkKeys: true).Stage(older, PluginPackage.ThisPlatform);
        PluginInstaller.Finish(Plugins);

        var window = Open();
        var dialog = Dropped(window, Write(Packages.For("win", "osx", "linux")));
        var installed = older.DescriptionFor(PluginPackage.ThisPlatform)!.Version;
        var incoming = PluginPackage.Read(Packages.For("win")).Description("win").Version;

        Named(dialog, "install").Content.ShouldBe("Update");
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginReplacing").Text
            .ShouldBe($"Updates {Packages.Name} {installed}, which is installed now, to {incoming}.");

        Press(Named(dialog, "install"));
        Pump(() => !All<ModalOverlay>(window).Any());

        All<ReportLine>(window).Single().History.ShouldContain($"{Packages.Name} {incoming} is updated, and loads the next time Flyback starts.");
    }

    [AvaloniaFact]
    public void An_older_build_of_an_installed_plugin_is_offered_as_a_downgrade()
    {
        new PluginInstaller(Plugins, [], checkKeys: true).Stage(PluginPackage.Read(Packages.For("win", "osx", "linux")), PluginPackage.ThisPlatform);
        PluginInstaller.Finish(Plugins);

        var dialog = Dropped(Open(), Write(Packages.Sign(Packages.Unsigned(Packages.Older, "win", "osx", "linux"))));

        Named(dialog, "install").Content.ShouldBe("Downgrade");
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginReplacing").Text!.ShouldContain("with the older");
    }
}
