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

    private MainWindow Open(Action? relaunch = null)
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

        Texts(dialog).ShouldContain(Packages.Folder);
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginAdds").Text.ShouldBe("modules, presets");
        All<SelectableTextBlock>(dialog).Single(t => t.Name == "pluginReaches").Text.ShouldBe("nothing outside Flyback that it names");
        All<Border>(dialog).ShouldContain(b => b.Name == "pluginWarning");
        Named(dialog, "install").IsEnabled.ShouldBeTrue();
        Directory.Exists(Plugins).ShouldBeFalse("nothing is written before Install is pressed");

        Press(Named(dialog, "install"));
        Pump(() => !All<ModalOverlay>(window).Any());

        Directory.Exists(Path.Combine(Plugins, PluginInstaller.PendingName, Packages.Folder)).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Installing_with_restart_ticked_starts_Flyback_again_and_closes_this_window()
    {
        var relaunched = 0;
        var window = Open(() => relaunched++);
        var dialog = Dropped(window, Write(Packages.For("win", "osx", "linux")));

        All<CheckBox>(dialog).Single(c => c.Name == "restart").IsChecked.ShouldBe(true, "a plugin does nothing until Flyback starts again");

        Press(Named(dialog, "install"));
        Pump(() => relaunched > 0);

        relaunched.ShouldBe(1);
        window.IsVisible.ShouldBeFalse();
        Directory.Exists(Path.Combine(Plugins, PluginInstaller.PendingName, Packages.Folder)).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Installing_with_restart_unticked_leaves_the_window_open()
    {
        var relaunched = 0;
        var window = Open(() => relaunched++);
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
    public void Cancelling_installs_nothing()
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
}
