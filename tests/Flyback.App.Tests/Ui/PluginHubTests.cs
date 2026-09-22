using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.App.Tests.PluginPackages;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>The plugins window: what is installed and what the site offers, under one search.</summary>
public sealed class PluginHubTests : UiTest
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-hub-" + Guid.NewGuid().ToString("N"));

    private string Plugins => Path.Combine(folder, "plugins");

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static readonly HubInstalled Echoes = new(
        new ListedPlugin("Flyback.Plugins.Echoes", "Echoes", "1.0.0", "Ann", "Delays that repeat.", ["space"], ["Tape"]),
        Waiting: null, Loaded: true, Picture: null);

    private static readonly HubInstalled Grain = new(
        new ListedPlugin("Flyback.Plugins.Grain", "Grain", "2.0.0", "Bob", "Film grain.", ["picture"], ["Noise"]),
        Waiting: null, Loaded: true, Picture: null);

    private (PluginHub Hub, Window Window) Open(FakePluginSite site, Func<SitePlugin, Task<string?>>? install = null)
    {
        var hub = new PluginHub(site.Site(), () => Task.FromResult<IReadOnlyList<HubInstalled>>([Echoes, Grain]), install ?? (_ => Task.FromResult<string?>(null)));

        var content = new DockPanel();

        DockPanel.SetDock(hub.Header, Dock.Top);
        content.Children.Add(hub.Header);
        content.Children.Add(hub.View);

        var window = Show(content, width: 700);

        _ = hub.LoadAsync();
        Pump(() => Names(hub.View, "sitePlugins").Any() || Status(hub, "siteStatus") is not ("" or "Looking…"));
        Settle(window);

        return (hub, window);
    }

    /// <summary>
    /// The site answers on the thread pool, and a search waits a quarter of a second for
    /// typing to stop. Lays out <paramref name="window"/> as it goes, which is what builds the site's rows.
    /// </summary>
    private static void Pump(Func<bool> until, Window? window = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);

        while (!until() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            window?.UpdateLayout();
            Thread.Sleep(5);
        }
    }

    /// <summary>The names on a list's rows, in order; for the site's, only the rows built.</summary>
    private static IEnumerable<string?> Names(Control view, string list)
    {
        // The site's rows are built by layout, which pumping the dispatcher does not run.
        view.UpdateLayout();

        IEnumerable<Control> rows = All<Control>(view).Single(c => c.Name == list) switch
        {
            ItemsControl site => site.GetRealizedContainers().OrderBy(site.IndexFromContainer),
            Panel installed => installed.Children,
            var other => throw new InvalidOperationException(other.GetType().Name),
        };

        return rows.SelectMany(row => All<TextBlock>(row).Where(t => t.Name == "pluginName")).Select(t => t.Text);
    }

    private static string? Status(PluginHub hub, string name) => All<TextBlock>(hub.View).Single(t => t.Name == name).Text;

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void It_lists_what_is_installed_and_what_the_site_offers()
    {
        using var site = new FakePluginSite(new Shared("r1", "Ripple"), new Shared("s1", "Shimmer"));
        var (hub, _) = Open(site);

        Names(hub.View, "installedPlugins").ShouldBe(["Echoes", "Grain"]);
        Names(hub.View, "sitePlugins").ShouldBe(["Ripple", "Shimmer"]);
    }

    [AvaloniaFact]
    public void Typing_narrows_the_installed_plugins_and_asks_the_site_the_same()
    {
        using var site = new FakePluginSite(new Shared("r1", "Ripple", Tags: ["water"]), new Shared("s1", "Shimmer", Modules: ["Tape"]));
        var (hub, _) = Open(site);

        hub.Search.Text = "tape";
        Pump(() => site.Asked.Count > 1 && Names(hub.View, "sitePlugins").Count() == 1);

        Names(hub.View, "installedPlugins").ShouldBe(["Echoes"]);
        Names(hub.View, "sitePlugins").ShouldBe(["Shimmer"]);
        site.Asked[^1].Query.ShouldContain("q=tape");
    }

    [AvaloniaFact]
    public void A_tag_narrows_both_lists_until_it_is_taken_off()
    {
        using var site = new FakePluginSite(new Shared("r1", "Ripple", Tags: ["water"]), new Shared("s1", "Shimmer", Tags: ["space"]));
        var (hub, window) = Open(site);

        var space = All<Button>(hub.View).First(b => b.Content as string == "space");

        Press(space);
        Pump(() => Names(hub.View, "sitePlugins").Count() == 1);

        Names(hub.View, "installedPlugins").ShouldBe(["Echoes"]);
        Names(hub.View, "sitePlugins").ShouldBe(["Shimmer"]);
        site.Asked[^1].Query.ShouldContain("tag=space");

        Settle(window);
        Press(All<Button>(hub.Header).Single(b => b.Name == "tagFilter"));
        Pump(() => Names(hub.View, "sitePlugins").Count() == 2);

        Names(hub.View, "installedPlugins").ShouldBe(["Echoes", "Grain"]);
        Names(hub.View, "sitePlugins").ShouldBe(["Ripple", "Shimmer"]);
    }

    [AvaloniaFact]
    public void The_site_is_listed_a_page_at_a_time_and_more_adds_the_next()
    {
        using var site = new FakePluginSite(new Shared("a", "Aurora"), new Shared("b", "Bloom"), new Shared("c", "Cinder")) { PageSize = 2 };
        var (hub, _) = Open(site);

        Names(hub.View, "sitePlugins").ShouldBe(["Aurora", "Bloom"]);

        var more = All<Button>(hub.View).Single(b => b.Name == "moreSite");

        more.IsVisible.ShouldBeTrue();

        Press(more);
        Pump(() => Names(hub.View, "sitePlugins").Count() == 3);

        Names(hub.View, "sitePlugins").ShouldBe(["Aurora", "Bloom", "Cinder"]);
        site.Asked[^1].Query.ShouldContain("page=2");
        more.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_long_list_from_the_site_builds_only_the_rows_in_view()
    {
        using var site = new FakePluginSite([.. Enumerable.Range(0, 200).Select(i => new Shared($"p{i}", $"Plugin {i:000}"))]) { PageSize = 200 };

        var window = Owned(new MainWindow(pluginFolder: Plugins, pluginSite: FakePluginSite.Root) { SiteHttp = new HttpClient(site) });

        window.Show();
        Settle(window);

        Press(All<Button>(window).Single(b => b.Name == "plugins"));
        Pump(() => All<ItemsControl>(window).Any(l => l.Name == "sitePlugins" && l.ItemCount == 200), window);
        Settle(window);

        var list = All<ItemsControl>(window).Single(l => l.Name == "sitePlugins");
        var built = list.GetRealizedContainers().Count();

        built.ShouldBeGreaterThan(0);
        built.ShouldBeLessThan(40, "only what fits the window is built");

        var scroller = list.FindAncestorOfType<ScrollViewer>()!;

        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Settle(window);
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Settle(window);

        Names(list, "sitePlugins").ShouldContain("Plugin 199");
        list.GetRealizedContainers().Count().ShouldBeLessThan(40);
    }

    [AvaloniaFact]
    public void A_site_plugin_is_offered_as_an_install_an_update_or_nothing()
    {
        using var site = new FakePluginSite(
            new Shared("n1", "Ripple"),
            new Shared("e2", "Echoes", Assembly: "Flyback.Plugins.Echoes", Version: "1.1.0"),
            new Shared("g2", "Grain", Assembly: "flyback.plugins.grain", Version: "2.0.0"));
        var (hub, _) = Open(site);

        string Offered(string id) => All<Grid>(hub.View).Single(g => g.Tag is SitePlugin p && p.Id == id) is var row
            && All<Button>(row).FirstOrDefault(b => b.Name == "install") is { } button
                ? (string)button.Content!
                : All<TextBlock>(row).Last().Text!;

        Offered("n1").ShouldBe("Install");
        Offered("e2").ShouldBe("Update");
        Offered("g2").ShouldBe("Installed");
    }

    [AvaloniaFact]
    public void An_unreachable_site_says_so_and_still_lists_what_is_installed()
    {
        var hub = new PluginHub(
            new PluginSite(new HttpClient(new Unreachable()), FakePluginSite.Root),
            () => Task.FromResult<IReadOnlyList<HubInstalled>>([Echoes]),
            _ => Task.FromResult<string?>(null));

        var window = Show(hub.View, width: 700);

        _ = hub.LoadAsync();
        Pump(() => Status(hub, "siteStatus")!.Contains("did not answer"));
        Settle(window);

        Status(hub, "siteStatus").ShouldBe($"The plugin site at {FakePluginSite.Root} did not answer.");
        Names(hub.View, "installedPlugins").ShouldBe(["Echoes"]);
    }

    [AvaloniaFact]
    public void The_toolbar_opens_the_plugins_window_and_installs_from_the_site_only_when_asked()
    {
        var package = Packages.For("win", "osx", "linux");
        var version = PluginPackage.ReadAsync(new MemoryStream(package)).Result.DescriptionFor(PluginPackage.ThisPlatform)!.Version;

        using var site = new FakePluginSite(new Shared("p1", "Picture", Assembly: Packages.Folder, Version: version, Package: package));

        var window = Owned(new MainWindow(pluginFolder: Plugins, pluginSite: FakePluginSite.Root) { SiteHttp = new HttpClient(site) });

        window.Show();
        Settle(window);

        Press(All<Button>(window).Single(b => b.Name == "plugins"));
        Pump(() => All<Button>(window).Any(b => b.Name == "install" && b.Tag is SitePlugin), window);
        Settle(window);

        var install = All<Button>(window).Single(b => b.Name == "install" && b.Tag is SitePlugin);

        Press(install);
        Pump(() => All<Border>(window).Any(b => b.Name == "pluginWarning"), window);
        Settle(window);

        All<ModalOverlay>(window).Count().ShouldBe(2, "the install question sits over the plugins window");
        Directory.Exists(Plugins).ShouldBeFalse("nothing is written before Install is pressed");

        Press(All<Button>(All<ModalOverlay>(window).Last()).Single(b => b.Name == "install"));

        // The row stops offering it once what is installed has been read again.
        Pump(() => !All<Button>(window).Any(b => b.Name == "install" && b.Tag is SitePlugin), window);

        Directory.Exists(Path.Combine(Plugins, PluginInstaller.PendingName, Packages.Folder)).ShouldBeTrue();
        All<TextBlock>(window).Single(t => t.Name == "pluginNotice").Text.ShouldEndWith("loads the next time Flyback starts.");
        All<TextBlock>(window).ShouldContain(t => t.Name == "pluginState" && t.Text == "Loads at the next start");
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made.");
    }
}
