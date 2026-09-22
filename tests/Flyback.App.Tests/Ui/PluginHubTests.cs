using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    private (PluginHub Hub, Window Window) Open(
        FakePluginSite site,
        Func<SitePlugin, Task<string?>>? install = null,
        IReadOnlyList<SitePlugin>? needed = null)
    {
        var hub = new PluginHub(
            site.Site(),
            () => Task.FromResult<IReadOnlyList<HubInstalled>>([Echoes, Grain]),
            install ?? (_ => Task.FromResult<string?>(null)),
            show: null,
            needed);

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

    /// <summary>What a patch was short of, which one search box could not ask for.</summary>
    [AvaloniaFact]
    public async Task The_plugins_a_patch_needs_are_listed_first_and_said_to_be_why()
    {
        using var site = new FakePluginSite(new Shared("r1", "Ripple"), new Shared("s1", "Shimmer"), new Shared("t1", "Tape"));

        var found = (await site.Site().SearchAsync(null, null, 1, CancellationToken.None)).Items;
        var needed = found.Where(p => p.Plugin.Name is "Tape" or "Shimmer").ToList();

        var (hub, window) = Open(site, needed: needed);

        Names(hub.View, "sitePlugins").ShouldBe(["Shimmer", "Tape", "Ripple"], "the two wanted first, each once");
        // The notice is in the header, which stays put while the lists scroll.
        All<TextBlock>(hub.Header).Single(t => t.Name == "pluginNotice").Text
            .ShouldBe("Shimmer and Tape are the plugins the patch needs, listed first.");

        // Typing is asking for something else, so the two stop being pinned.
        hub.Search.Text = "Ripple";
        Pump(() => Names(hub.View, "sitePlugins").Count() == 1, window);

        Names(hub.View, "sitePlugins").ShouldBe(["Ripple"]);
    }

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

        var window = Owned(new MainWindow(pluginFolder: Plugins, presetSite: FakePluginSite.Root) { SiteHttp = new HttpClient(site) });

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
    public void A_site_plugin_is_offered_as_a_click_to_install_an_update_or_nothing()
    {
        using var site = new FakePluginSite(
            new Shared("n1", "Ripple"),
            new Shared("e2", "Echoes", Assembly: "Flyback.Plugins.Echoes", Version: "1.1.0"),
            new Shared("g2", "Grain", Assembly: "flyback.plugins.grain", Version: "2.0.0"));
        var (hub, _) = Open(site);

        Grid RowFor(string id) => All<Grid>(hub.View).Single(g => g.Tag is SitePlugin p && p.Id == id);
        string? Offered(string id) => All<TextBlock>(RowFor(id)).SingleOrDefault(t => t.Name == "siteState")?.Text;

        Offered("n1").ShouldBeNull("nothing is installed, so a click on the row installs it");
        ((string)ToolTip.GetTip(RowFor("n1"))!).ShouldContain("Download Ripple");
        Offered("e2").ShouldBe("Update available");
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

        var window = Owned(new MainWindow(pluginFolder: Plugins, presetSite: FakePluginSite.Root) { SiteHttp = new HttpClient(site) });

        window.Show();
        Settle(window);

        Press(All<Button>(window).Single(b => b.Name == "plugins"));
        Pump(() => All<Grid>(window).Any(g => g.Tag is SitePlugin), window);
        Settle(window);

        var row = All<Grid>(window).Single(g => g.Tag is SitePlugin);

        Click(window, row);
        Pump(() => All<Border>(window).Any(b => b.Name == "pluginWarning"), window);
        Settle(window);

        All<ModalOverlay>(window).Count().ShouldBe(2, "the install question sits over the plugins window");
        Directory.Exists(Plugins).ShouldBeFalse("nothing is written before Install is pressed");

        Press(All<Button>(All<ModalOverlay>(window).Last()).Single(b => b.Name == "install"));

        // What is installed is read again once it is.
        Pump(() => All<TextBlock>(window).Any(t => t.Name == "pluginState" && t.Text == "Loads at the next start"), window);

        All<TextBlock>(window).ShouldContain(t => t.Name == "siteState" && t.Text == "Installed");

        Directory.Exists(Path.Combine(Plugins, PluginInstaller.PendingName, Packages.Folder)).ShouldBeTrue();
        All<TextBlock>(window).Single(t => t.Name == "pluginNotice").Text.ShouldEndWith("loads the next time Flyback starts.");
        All<TextBlock>(window).ShouldContain(t => t.Name == "pluginState" && t.Text == "Loads at the next start");
    }

    [AvaloniaFact]
    public void Clicking_an_installed_plugin_shows_what_it_is()
    {
        var package = Picture();
        var described = package.DescriptionFor(PluginPackage.ThisPlatform)!;

        new PluginInstaller(Plugins, [], checkKeys: false).Stage(package, PluginPackage.ThisPlatform);

        var window = OpenPlugins();

        Click(window, InstalledRow(window));
        Pump(() => All<StackPanel>(window).Any(p => p.Name == "pluginInstalled"), window);
        Settle(window);

        var shown = All<StackPanel>(window).Single(p => p.Name == "pluginInstalled");

        All<ModalOverlay>(window).Count().ShouldBe(2, "it sits over the plugins window");
        All<TextBlock>(shown).Single(t => t.Name == "pluginName").Text.ShouldBe(described.Name);
        All<TextBlock>(shown).Single(t => t.Name == "pluginAdds").Text.ShouldNotBeNullOrEmpty();
        All<TextBlock>(shown).Single(t => t.Name == "pluginOrigin").Text.ShouldBe("from a package");
        All<TextBlock>(shown).Single(t => t.Name == "pluginState").Text.ShouldBe("Loads at the next start");

        Press(All<Button>(shown).Single(b => b.Name == "close"));
        Settle(window);

        All<ModalOverlay>(window).Count().ShouldBe(1, "closing it leaves the plugins window up");
    }

    /// <summary>A click on <paramref name="control"/>, clear of anything on it that is a button.</summary>
    private static void Click(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(20, 20), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    private MainWindow OpenPlugins(FakePluginSite? site = null)
    {
        var window = Owned(site is null
            ? new MainWindow(pluginFolder: Plugins)
            : new MainWindow(pluginFolder: Plugins, presetSite: FakePluginSite.Root) { SiteHttp = new HttpClient(site) });

        window.Show();
        Settle(window);

        Press(All<Button>(window).Single(b => b.Name == "plugins"));
        Pump(() => All<TextBlock>(window).Any(t => t.Name == "pluginState")
            || All<TextBlock>(window).Any(t => t.Name == "installedStatus" && t.IsVisible), window);
        Pump(() => site is null || All<Grid>(window).Any(g => g.Tag is SitePlugin), window);
        Settle(window);

        return window;
    }

    private static PluginPackage Picture() => PluginPackage.ReadAsync(new MemoryStream(Packages.For("win", "osx", "linux"))).Result;

    private static Grid InstalledRow(Window window) =>
        All<StackPanel>(window).Single(p => p.Name == "installedPlugins").Children.OfType<Grid>().Single();

    [AvaloniaFact]
    public void Clicking_a_site_plugin_asks_about_installing_it()
    {
        using var site = new FakePluginSite(new Shared("p1", "Picture", Assembly: Packages.Folder, Version: "1.0.0", Package: Packages.For("win", "osx", "linux")));
        var window = OpenPlugins(site);

        Click(window, All<Grid>(window).Single(g => g.Tag is SitePlugin));
        Pump(() => All<Border>(window).Any(b => b.Name == "pluginWarning"), window);
        Settle(window);

        All<ModalOverlay>(window).Count().ShouldBe(2);
        All<Button>(All<ModalOverlay>(window).Last()).ShouldNotContain(b => b.Name == "remove", "it is not installed");
    }

    [AvaloniaFact]
    public void An_installed_plugin_the_site_has_newer_offers_the_update_and_asks_about_it()
    {
        var package = Picture();
        var version = package.DescriptionFor(PluginPackage.ThisPlatform)!.Version;

        new PluginInstaller(Plugins, [], checkKeys: false).Stage(package, PluginPackage.ThisPlatform);

        using var site = new FakePluginSite(new Shared("p2", "Picture", Assembly: Packages.Folder, Version: "99.0.0", Package: Packages.For("win", "osx", "linux")));
        var window = OpenPlugins(site);

        Click(window, InstalledRow(window));
        Pump(() => All<StackPanel>(window).Any(p => p.Name == "pluginInstalled"), window);
        Settle(window);

        var shown = All<StackPanel>(window).Single(p => p.Name == "pluginInstalled");

        All<TextBlock>(shown).Single(t => t.Name == "pluginNewer").Text.ShouldBe("The plugin site has Picture 99.0.0.");
        version.ShouldNotBe("99.0.0");

        Press(All<Button>(shown).Single(b => b.Name == "update"));
        Pump(() => All<Border>(window).Any(b => b.Name == "pluginWarning"), window);
        Settle(window);

        All<ModalOverlay>(window).Count().ShouldBe(2, "the downloaded package is asked about over the plugins window");
        All<Button>(All<ModalOverlay>(window).Last()).ShouldContain(b => b.Name == "remove", "it is installed");
    }

    [AvaloniaFact]
    public void Removing_a_plugin_waiting_to_be_installed_takes_it_away_at_once()
    {
        new PluginInstaller(Plugins, [], checkKeys: false).Stage(Picture(), PluginPackage.ThisPlatform);

        var window = OpenPlugins();

        Click(window, InstalledRow(window));
        Pump(() => All<StackPanel>(window).Any(p => p.Name == "pluginInstalled"), window);
        Settle(window);

        var shown = All<StackPanel>(window).Single(p => p.Name == "pluginInstalled");

        All<TextBlock>(shown).ShouldNotContain(t => t.Name == "pluginNewer");
        Press(All<Button>(shown).Single(b => b.Name == "remove"));
        Pump(() => All<TextBlock>(window).Any(t => t.Name == "installedStatus" && t.IsVisible), window);

        All<TextBlock>(window).Single(t => t.Name == "pluginNotice").Text.ShouldEndWith("is removed.");
        new PluginInstaller(Plugins, [], checkKeys: false).Waiting().ShouldBeEmpty();
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made.");
    }

    [AvaloniaFact]
    public void A_site_plugin_is_reported_to_the_site_with_the_reason_picked()
    {
        using var site = new FakePluginSite(new Shared("r1", "Ripple"));
        var (hub, window) = Open(site);

        Press(All<Button>(hub.View).Single(b => b.Name == "report"));
        Pump(() => All<RadioButton>(window).Any());

        All<RadioButton>(window).Single(r => (string?)r.Tag == "harmful").IsChecked = true;
        All<TextBox>(window).Single(t => t.Name == "details").Text = "  It deleted my presets.  ";
        Press(All<Button>(window).Single(b => b.Name == "send"));
        Pump(() => hub.Header is Panel header && All<TextBlock>(header).Single(t => t.Name == "pluginNotice").IsVisible);

        var (path, body) = site.Reports.ShouldHaveSingleItem();

        path.ShouldBe("/api/v1/plugins/r1/reports");
        body.ShouldContain("\"reason\":\"harmful\"");
        body.ShouldContain("\"details\":\"It deleted my presets.\"");
        All<TextBlock>(hub.Header).Single(t => t.Name == "pluginNotice").Text.ShouldBe("Reported “Ripple” to the preset site's admin.");
        All<RadioButton>(window).ShouldBeEmpty("the dialog is down");
    }

    [AvaloniaFact]
    public void A_report_the_site_does_not_take_says_so_and_can_be_sent_again()
    {
        using var site = new FakePluginSite(new Shared("r1", "Ripple")) { RefuseReports = true };
        var (hub, window) = Open(site);

        Press(All<Button>(hub.View).Single(b => b.Name == "report"));
        Pump(() => All<RadioButton>(window).Any());

        var send = All<Button>(window).Single(b => b.Name == "send");

        send.IsEnabled.ShouldBeFalse("nothing is picked yet");

        All<RadioButton>(window).First().IsChecked = true;
        Press(send);
        Pump(() => All<TextBlock>(window).Single(t => t.Name == "reportStatus").IsVisible);

        All<TextBlock>(window).Single(t => t.Name == "reportStatus").Text!.ShouldStartWith("The report was not sent.");
        send.IsEnabled.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_site_plugin_shows_its_stars_and_offers_no_way_to_give_them()
    {
        using var site = new FakePluginSite(new Shared("r1", "Ripple", Average: 4.4, Ratings: 7), new Shared("n1", "Newt"));
        var (hub, _) = Open(site);

        string Said(string id) => All<TextBlock>(All<Grid>(hub.View).Single(g => g.Tag is SitePlugin p && p.Id == id))
            .Single(t => t.Name == "siteRating").Inlines!.Text!;

        Said("r1").ShouldBe("★★★★★  4.4 (7 ratings)");
        Said("n1").ShouldBe("★★★★★  Not rated yet");
        ((SitePlugin)All<Grid>(hub.View).First(g => g.Tag is SitePlugin).Tag!).Rating.Stars.ShouldBe(4);
        site.Asked.ShouldNotContain(u => u.AbsolutePath.EndsWith("/rating", StringComparison.Ordinal), "ratings are given on the site");
    }
}
